namespace Divoom;

/// <summary>Serializes commands and owns sessions beyond the lifetime of individual HTTP requests.</summary>
public sealed class DivoomAudioPlayer(IAudioPlaybackBackend backend, TimeSpan? startupTimeout = null) : IDivoomAudioPlayer
{
    private readonly SemaphoreSlim commands = new(1, 1);
    private readonly object stateLock = new();
    private readonly TimeSpan timeout = startupTimeout ?? TimeSpan.FromSeconds(20);
    private AudioPlaybackStatus status = new("stopped", 0.25);
    private AudioPlaybackRun? current;
    private bool disposed;

    public AudioPlaybackStatus Status { get { lock (stateLock) return status; } }

    public async Task StartAsync(Uri source, double volume = 0.25, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsAbsoluteUri || source.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("An absolute HTTPS stream URL is required.", nameof(source));
        ValidateVolume(volume);
        await commands.WaitAsync(cancellationToken);
        AudioPlaybackRun? previous = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            AudioPlaybackRun? existing;
            lock (stateLock)
            {
                existing = current?.Source == source && status.State is "starting" or "buffering" or "playing" ? current : null;
            }
            if (existing is not null)
            {
                existing.Session.Volume = volume;
                lock (stateLock) status = status with { Volume = volume };
                return;
            }
            // Keep the old station if preparation or cancellation fails.
            var session = await backend.OpenAsync(source, volume, cancellationToken);
            if (cancellationToken.IsCancellationRequested) { session.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
            var run = new AudioPlaybackRun(session, source);
            run.Handler = state => OnStateChanged(run, state);
            session.StateChanged += run.Handler;
            lock (stateLock)
            {
                previous = current;
                current = run;
                status = new("starting", volume);
            }
            try
            {
                Release(previous);
                session.Play();
                OnStateChanged(run, session.State);
                run.Monitor = MonitorAsync(run);
            }
            catch (Exception error)
            {
                lock (stateLock) { current = null; status = new("failed", volume, "stream_failed"); }
                Release(run);
                run.Cancellation.Dispose();
                throw new AudioUnavailableException("The audio stream could not be started.", error);
            }
        }
        finally
        {
            commands.Release();
            if (previous is not null) await FinishAsync(previous);
        }
    }

    public async Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        ValidateVolume(volume);
        await commands.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            AudioPlaybackRun? run;
            lock (stateLock) run = current;
            if (run is not null) run.Session.Volume = volume;
            lock (stateLock) status = status with { Volume = volume };
        }
        finally { commands.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await commands.WaitAsync(cancellationToken);
        AudioPlaybackRun? run;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            lock (stateLock) { run = current; current = null; status = new("stopped", status.Volume); }
            Release(run);
        }
        finally { commands.Release(); }
        if (run is not null) await FinishAsync(run);
    }

    public async ValueTask DisposeAsync()
    {
        await commands.WaitAsync();
        AudioPlaybackRun? run;
        try
        {
            if (disposed) return;
            disposed = true;
            lock (stateLock) { run = current; current = null; status = new("stopped", status.Volume); }
            Release(run);
        }
        finally { commands.Release(); }
        if (run is not null) await FinishAsync(run);
    }

    private void OnStateChanged(AudioPlaybackRun run, string state)
    {
        lock (stateLock)
        {
            if (current != run || status.State is "failed" or "stopped") return;
            status = new(state, status.Volume, state == "failed" ? "stream_failed" : null);
            if (state == "playing") run.Ready.TrySetResult();
            if (state is "failed" or "stopped") { run.Ready.TrySetResult(); run.Ended.TrySetResult(); }
        }
    }

    private async Task MonitorAsync(AudioPlaybackRun run)
    {
        var token = run.Cancellation.Token;
        string? failure = null;
        try
        {
            try { await run.Ready.Task.WaitAsync(timeout, token); }
            catch (TimeoutException) { failure = "startup_timeout"; }
            if (failure is null) await run.Ended.Task.WaitAsync(token);
            await commands.WaitAsync(token);
            try
            {
                lock (stateLock)
                {
                    if (current != run) return;
                    current = null;
                    if (failure is not null) status = new("failed", status.Volume, failure);
                }
                Release(run);
                run.Cancellation.Dispose();
            }
            finally { commands.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static void Release(AudioPlaybackRun? run)
    {
        if (run is null) return;
        run.Cancellation.Cancel();
        run.Session.StateChanged -= run.Handler;
        run.Session.Dispose();
    }

    private static async Task FinishAsync(AudioPlaybackRun run)
    {
        await run.Monitor;
        run.Cancellation.Dispose();
    }

    private static void ValidateVolume(double volume)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 1.");
    }
}
