using Divoom;
using NUnit.Framework;

public sealed class AudioPlayerTests
{
    private static readonly Uri Source = new("https://radio.example/stream.mp3");

    [Test]
    public async Task PlaybackOutlivesCommandAndRepeatedStartReusesSession()
    {
        var backend = new FakeAudioBackend();
        await using var player = new DivoomAudioPlayer(backend);
        using var request = new CancellationTokenSource();
        await player.StartAsync(Source, 0.25, request.Token);
        request.Cancel();
        await player.StartAsync(Source, 0.5);
        Assert.That(backend.Sessions, Has.Count.EqualTo(1));
        Assert.That(player.Status, Is.EqualTo(new AudioPlaybackStatus("playing", 0.5)));
        Assert.That(backend.Sessions[0].Disposals, Is.Zero);
        await player.StopAsync();
        await player.StopAsync();
        Assert.That(backend.Sessions[0].Disposals, Is.EqualTo(1));
        Assert.That(player.Status.State, Is.EqualTo("stopped"));
    }

    [Test]
    public async Task VolumeCommandsAllowBackendCallbacksWhileWaitingForAcknowledgement()
    {
        var backend = new FakeAudioBackend();
        await using var player = new DivoomAudioPlayer(backend);
        await player.StartAsync(Source);
        backend.Sessions[0].OnVolumeSet = () => Task.Run(() => backend.Sessions[0].Emit("playing"))
            .WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        await player.SetVolumeAsync(0.4);
        await player.StartAsync(Source, 0.5);
        Assert.That(player.Status, Is.EqualTo(new AudioPlaybackStatus("playing", 0.5)));
        Assert.That(backend.Sessions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ReplacementReleasesOldSessionAndIgnoresLateCallbacks()
    {
        var backend = new FakeAudioBackend();
        await using var player = new DivoomAudioPlayer(backend);
        await player.StartAsync(Source);
        var lateCallback = backend.Sessions[0].SnapshotHandlers();
        await player.StartAsync(new Uri("https://radio.example/other.mp3"));
        lateCallback!("failed");
        Assert.That(backend.Sessions[0].Disposals, Is.EqualTo(1));
        Assert.That(backend.Sessions[1].Disposals, Is.Zero);
        Assert.That(player.Status.State, Is.EqualTo("playing"));
    }

    [Test]
    public async Task CancelledPreparationReleasesNewSessionAndKeepsCurrentStation()
    {
        var backend = new FakeAudioBackend();
        await using var player = new DivoomAudioPlayer(backend);
        await player.StartAsync(Source);
        using var request = new CancellationTokenSource();
        backend.OnOpen = request.Cancel;
        Assert.ThrowsAsync<OperationCanceledException>(() => player.StartAsync(new Uri("https://radio.example/other.mp3"), cancellationToken: request.Token));
        Assert.That(backend.Sessions[0].Disposals, Is.Zero);
        Assert.That(backend.Sessions[1].Disposals, Is.EqualTo(1));
        Assert.That(player.Status.State, Is.EqualTo("playing"));
    }

    [Test]
    public async Task FailedPreparationKeepsCurrentStation()
    {
        var backend = new FakeAudioBackend();
        await using var player = new DivoomAudioPlayer(backend);
        await player.StartAsync(Source);
        backend.Error = new AudioUnavailableException("output vanished");
        Assert.ThrowsAsync<AudioUnavailableException>(() => player.StartAsync(new Uri("https://radio.example/other.mp3")));
        Assert.That(backend.Sessions[0].Disposals, Is.Zero);
        Assert.That(player.Status.State, Is.EqualTo("playing"));
    }

    [Test]
    public async Task StartupTimeoutReleasesSessionAndReportsStableError()
    {
        var backend = new FakeAudioBackend { AutoPlay = false };
        await using var player = new DivoomAudioPlayer(backend, TimeSpan.FromMilliseconds(50));
        await player.StartAsync(Source);
        await backend.Sessions[0].Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(player.Status, Is.EqualTo(new AudioPlaybackStatus("failed", 0.25, "startup_timeout")));
    }

    [TestCase("failed", "stream_failed")]
    [TestCase("stopped", null)]
    public async Task TerminalEventReleasesSession(string state, string? error)
    {
        var backend = new FakeAudioBackend();
        await using var player = new DivoomAudioPlayer(backend);
        await player.StartAsync(Source);
        var lateCallback = backend.Sessions[0].SnapshotHandlers();
        backend.Sessions[0].Emit(state);
        lateCallback!("playing");
        await backend.Sessions[0].Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(player.Status, Is.EqualTo(new AudioPlaybackStatus(state, 0.25, error)));
        await player.DisposeAsync();
        Assert.That(backend.Sessions[0].Disposals, Is.EqualTo(1));
    }
}
