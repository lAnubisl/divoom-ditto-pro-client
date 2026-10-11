using Divoom;
using NUnit.Framework;

public sealed class LinuxAudioBackendTests
{
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("auto")]
    [TestCase("default")]
    [TestCase("@DEFAULT_SINK@")]
    public void DefaultOutputIsRejectedBeforeOpeningPlayer(string sink)
    {
        var backend = new LinuxAudioPlaybackBackend(sink, bluetooth: null);
        var error = Assert.ThrowsAsync<AudioUnavailableException>(() => backend.OpenAsync(new Uri("https://radio.example/live"), 0.25, cancellationToken: CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("explicit PulseAudio sink"));
    }

    [Test]
    public void CancelledOpenDoesNotLaunchPlayer()
    {
        var backend = new LinuxAudioPlaybackBackend("bluez_output.example.1", bluetooth: null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => backend.OpenAsync(new Uri("https://radio.example/live"), 0.25, cancellation.Token));
    }
}
