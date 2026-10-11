namespace Divoom;

/// <summary>Owns playback and recovery beyond individual HTTP requests.</summary>
public sealed class DivoomAudioPlayer : IDivoomAudioPlayer
{
    private readonly IAudioPlaybackBackend backend;
    private readonly SemaphoreSlim commands = new(1, 1);
    private readonly object stateLock = new();
    private readonly TimeSpan timeout;
    private readonly bool recover;
    private readonly TimeSpan[] delays;
    private readonly Action<AudioPlaybackStatus>? reportRecovery;
    private AudioPlaybackStatus status = new("stopped", 0.25, Error: null, RetryAttempts: 0, LastError: null);
    private AudioPlaybackRun? current;
    private AudioPlaybackIntent? intent;
    private bool disposed;

    public DivoomAudioPlayer(IAudioPlaybackBackend backend, TimeSpan startupTimeout, AudioRecoveryOptions recovery,
        Action<AudioPlaybackStatus>? reportRecovery)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(recovery);
        this.backend = backend;
        this.reportRecovery = reportRecovery;
        timeout = startupTimeout;
        recover = recovery.Enabled;
        delays = recovery.RetryDelays.ToArray();
        if (timeout <= TimeSpan.Zero || delays.Length == 0 || delays.Any(d => d <= TimeSpan.Zero || d.TotalMilliseconds > int.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(recovery), "Timeout and retry delays must be positive, bounded durations.");
    }

    public AudioPlaybackStatus Status { get { lock (stateLock) return status; } }

    public async Task StartAsync(Uri source, double volume, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsAbsoluteUri || source.Scheme != Uri.UriSchemeHttps && source.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException("An absolute HTTP or HTTPS stream URL is required.", nameof(source));
        ValidateVolume(volume);
        await commands.WaitAsync(cancellationToken);
        AudioPlaybackIntent? previous = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (intent?.Source == source)
            {
                if (current is not null && !current.Ended.Task.IsCompleted) current.Session.Volume = volume;
                lock (stateLock) status = status with { Volume = volume };
                return;
            }
            IAudioPlaybackSession? session = null;
            // A failed replacement preserves an existing healthy stream.
            try { session = await backend.OpenAsync(source, volume, cancellationToken); }
            catch (AudioUnavailableException) when (recover && current is null && !cancellationToken.IsCancellationRequested) { }
            if (cancellationToken.IsCancellationRequested) { session?.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
            previous = intent;
            previous?.Cancellation.Cancel();
            var oldRun = current;
            var next = new AudioPlaybackIntent(source);
            lock (stateLock)
            {
                intent = next;
                current = null;
                status = session is null ? new("reconnecting", volume, "output_unavailable", LastError: "output_unavailable", RetryAttempts: 0) : new("starting", volume, Error: null, RetryAttempts: 0, LastError: null);
            }
            Release(oldRun);
            var run = session is null ? null : Install(session, next);
            next.Worker = MonitorAsync(next, run);
            if (session is null) ReportRecovery();
        }
        finally
        {
            commands.Release();
            if (previous is not null) await FinishAsync(previous);
        }
    }

    public async Task SetVolumeAsync(double volume, CancellationToken cancellationToken)
    {
        ValidateVolume(volume);
        await commands.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current is not null && !current.Ended.Task.IsCompleted) current.Session.Volume = volume;
            lock (stateLock) status = status with { Volume = volume };
        }
        finally { commands.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await commands.WaitAsync(cancellationToken);
        AudioPlaybackIntent? previous;
        try { ObjectDisposedException.ThrowIf(disposed, this); previous = Clear(); }
        finally { commands.Release(); }
        if (previous is not null) await FinishAsync(previous);
    }

    public async ValueTask DisposeAsync()
    {
        await commands.WaitAsync();
        AudioPlaybackIntent? previous;
        try { if (disposed) return; disposed = true; previous = Clear(); }
        finally { commands.Release(); }
        if (previous is not null) await FinishAsync(previous);
    }

    // Called only while commands is held. Session callbacks never acquire commands.
    private AudioPlaybackIntent? Clear()
    {
        var previous = intent;
        previous?.Cancellation.Cancel();
        var run = current;
        lock (stateLock) { intent = null; current = null; status = new("stopped", status.Volume, Error: null, RetryAttempts: 0, LastError: null); }
        Release(run);
        return previous;
    }

    private AudioPlaybackRun Install(IAudioPlaybackSession session, AudioPlaybackIntent owner)
    {
        var run = new AudioPlaybackRun(session, owner.Source);
        run.Handler = state => OnStateChanged(run, state);
        session.StateChanged += run.Handler;
        lock (stateLock) { current = run; status = status with { State = "starting", Error = null }; }
        try { session.Play(); OnStateChanged(run, session.State); }
        catch { OnStateChanged(run, "failed"); }
        return run;
    }

    private void OnStateChanged(AudioPlaybackRun run, string state)
    {
        lock (stateLock)
        {
            if (current != run || run.Ended.Task.IsCompleted) return;
            status = status with { State = state, Error = state == "failed" ? "stream_failed" : null };
            if (state == "playing") run.Ready.TrySetResult();
            if (state is "failed" or "stopped") { run.Ready.TrySetResult(); run.Ended.TrySetResult(); }
        }
    }

    private async Task MonitorAsync(AudioPlaybackIntent owner, AudioPlaybackRun? run)
    {
        var token = owner.Cancellation.Token;
        var consecutive = 0;
        try
        {
            while (true)
            {
                var failure = "output_unavailable";
                if (run is not null)
                {
                    try
                    {
                        await run.Ready.Task.WaitAsync(timeout, token);
                        if (run.Session.State == "playing") { consecutive = 0; if (Status.RetryAttempts > 0) ReportRecovery(); }
                        await run.Ended.Task.WaitAsync(token);
                        failure = "stream_failed";
                    }
                    catch (TimeoutException) { failure = "startup_timeout"; }
                    await commands.WaitAsync(token);
                    try
                    {
                        if (intent != owner) return;
                        lock (stateLock)
                        {
                            current = null;
                            status = status with { State = recover ? "reconnecting" : (failure == "startup_timeout" ? "failed" : status.State),
                                Error = recover ? failure : (failure == "startup_timeout" ? failure : status.Error), LastError = recover ? failure : null };
                        }
                        Release(run);
                        run = null;
                        if (recover) ReportRecovery();
                        if (!recover) { lock (stateLock) intent = null; owner.Cancellation.Dispose(); return; }
                    }
                    finally { commands.Release(); }
                }

                await Task.Delay(delays[Math.Min(consecutive, delays.Length - 1)], token);
                consecutive = Math.Min(consecutive + 1, delays.Length - 1);
                double volume;
                lock (stateLock)
                {
                    if (intent != owner) return;
                    volume = status.Volume;
                    status = status with { RetryAttempts = status.RetryAttempts == int.MaxValue ? int.MaxValue : status.RetryAttempts + 1 };
                }
                ReportRecovery();
                IAudioPlaybackSession? prepared = null;
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                    attempt.CancelAfter(timeout);
                    await backend.PrepareRecoveryAsync(attempt.Token);
                    prepared = await backend.OpenAsync(owner.Source, volume, attempt.Token);
                    await commands.WaitAsync(token);
                    try
                    {
                        if (intent != owner) return;
                        // PATCH may have changed volume during asynchronous preparation.
                        prepared.Volume = Status.Volume;
                        run = Install(prepared, owner);
                        prepared = null;
                    }
                    finally { commands.Release(); }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    lock (stateLock)
                        if (intent == owner) status = status with { State = "reconnecting", Error = "output_unavailable", LastError = "output_unavailable" };
                    ReportRecovery();
                }
                finally { prepared?.Dispose(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static void Release(AudioPlaybackRun? run)
    {
        if (run is null) return;
        run.Session.StateChanged -= run.Handler;
        run.Session.Dispose();
    }

    private static async Task FinishAsync(AudioPlaybackIntent owner)
    {
        await owner.Worker;
        owner.Cancellation.Dispose();
    }

    private void ReportRecovery()
    {
        // Diagnostics must not interfere with playback ownership or cleanup.
        try { reportRecovery?.Invoke(Status); } catch { }
    }

    private static void ValidateVolume(double volume)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 1.");
    }
}
