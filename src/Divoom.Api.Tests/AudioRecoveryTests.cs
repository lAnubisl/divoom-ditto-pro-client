using Divoom;
using NUnit.Framework;

public sealed class AudioRecoveryTests
{
    private static readonly Uri Source = new("https://radio.example/live");
    private static readonly AudioRecoveryOptions Options = new() { RetryDelays = [TimeSpan.FromMilliseconds(20)] };

    [Test]
    public async Task OutputLossRetriesPreservesSourceAndLatestVolumeAndIgnoresOldCallbacks()
    {
        var backend = new FakeAudioBackend();
        await using var player = new DivoomAudioPlayer(backend, recovery: Options, startupTimeout: TimeSpan.FromSeconds(20), reportRecovery: null);
        await player.StartAsync(Source, 0.4, cancellationToken: CancellationToken.None);
        var old = backend.Sessions[0];
        var late = old.SnapshotHandlers();
        backend.Error = new AudioUnavailableException("private backend detail");
        old.Emit("failed");
        await WaitUntilAsync(() => player.Status.RetryAttempts >= 2);
        Assert.That(player.Status.State, Is.EqualTo("reconnecting"));
        Assert.That(old.Disposals, Is.EqualTo(1));
        await player.SetVolumeAsync(0.6, cancellationToken: CancellationToken.None);
        backend.Error = null;
        await WaitUntilAsync(() => player.Status.State == "playing");
        late!("failed");
        Assert.That(backend.Source, Is.EqualTo(Source));
        Assert.That(backend.Sessions.Last().Volume, Is.EqualTo(0.6));
        Assert.That(player.Status.Error, Is.Null);
        Assert.That(player.Status.LastError, Is.EqualTo("output_unavailable"));
        await player.StopAsync( cancellationToken: CancellationToken.None);
        Assert.That(backend.Sessions.All(s => s.Disposals == 1), Is.True);
    }

    [Test]
    public async Task InitiallyMissingOutputIsAcceptedAndRequestCancellationDoesNotStopRecovery()
    {
        var backend = new FakeAudioBackend { Error = new AudioUnavailableException("missing") };
        await using var player = new DivoomAudioPlayer(backend, recovery: Options, startupTimeout: TimeSpan.FromSeconds(20), reportRecovery: null);
        using var request = new CancellationTokenSource();
        await player.StartAsync(Source, cancellationToken: request.Token, volume: 0.25);
        request.Cancel();
        Assert.That(player.Status.State, Is.EqualTo("reconnecting"));
        await player.StartAsync(Source, 0.7, cancellationToken: CancellationToken.None);
        backend.Error = null;
        await WaitUntilAsync(() => player.Status.State == "playing");
        Assert.That(backend.Sessions, Has.Count.EqualTo(1));
        Assert.That(player.Status.Volume, Is.EqualTo(0.7));
    }

    [Test]
    public async Task StopCancelsRecoveryWhileBackendIsConnecting()
    {
        var backend = new FakeAudioBackend();
        await using var player = new DivoomAudioPlayer(backend, recovery: Options, startupTimeout: TimeSpan.FromSeconds(20), reportRecovery: null);
        await player.StartAsync(Source, volume: 0.25, cancellationToken: CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.OnRecovery = async token =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cancelled.TrySetResult(); }
        };
        backend.Sessions[0].Emit("failed");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await player.StopAsync( cancellationToken: CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(player.Status.State, Is.EqualTo("stopped"));
        Assert.That(backend.Sessions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ReplacementDisposesLateRecoverySessionWithoutPlayingOldStation()
    {
        var backend = new FakeAudioBackend();
        await using var player = new DivoomAudioPlayer(backend, recovery: Options, startupTimeout: TimeSpan.FromSeconds(20), reportRecovery: null);
        await player.StartAsync(Source, volume: 0.25, cancellationToken: CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IAudioPlaybackSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.OpenOverride = (uri, volume, _) =>
        {
            if (uri == Source) { entered.TrySetResult(); return release.Task; }
            return Task.FromResult<IAudioPlaybackSession>(new FakeAudioSession ( autoPlay: true){ Volume = volume });
        };
        backend.Sessions[0].Emit("failed");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var replacement = player.StartAsync(new Uri("https://radio.example/new"), 0.8, cancellationToken: CancellationToken.None);
        var late = new FakeAudioSession( autoPlay: true);
        release.TrySetResult(late);
        await replacement.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(late.Disposals, Is.EqualTo(1));
        Assert.That(late.State, Is.EqualTo("starting"));
        Assert.That(player.Status.State, Is.EqualTo("playing"));
        Assert.That(player.Status.Volume, Is.EqualTo(0.8));
    }

    [Test]
    public async Task StartupTimeoutAndNaturalEndBothRecover()
    {
        var backend = new FakeAudioBackend { AutoPlay = false };
        await using var player = new DivoomAudioPlayer(backend, TimeSpan.FromMilliseconds(40), Options, reportRecovery: null);
        await player.StartAsync(Source, volume: 0.25, cancellationToken: CancellationToken.None);
        await backend.Sessions[0].Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        backend.AutoPlay = true;
        await WaitUntilAsync(() => player.Status.State == "playing");
        var recovered = backend.Sessions.Last();
        recovered.Emit("stopped");
        await WaitUntilAsync(() => player.Status.State == "playing" && recovered.Disposals == 1);
        Assert.That(player.Status.RetryAttempts, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task DisposalCancelsRetries()
    {
        var backend = new FakeAudioBackend { Error = new AudioUnavailableException("missing") };
        var player = new DivoomAudioPlayer(backend, recovery: Options, startupTimeout: TimeSpan.FromSeconds(20), reportRecovery: null);
        await player.StartAsync(Source, volume: 0.25, cancellationToken: CancellationToken.None);
        await WaitUntilAsync(() => player.Status.RetryAttempts > 0);
        await player.DisposeAsync();
        await player.DisposeAsync();
        Assert.That(player.Status, Is.EqualTo(new AudioPlaybackStatus("stopped", 0.25, Error: null, RetryAttempts: 0, LastError: null)));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(5, deadline.Token);
    }
}
