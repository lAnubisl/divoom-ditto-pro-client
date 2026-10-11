using Divoom;

namespace Divoom.Api;

internal sealed class UnavailableAudioPlayer(string reason) : IDivoomAudioPlayer
{
    public AudioPlaybackStatus Status { get; } = new("unavailable", 0.25, reason, RetryAttempts: 0, LastError: null);
    public Task StartAsync(Uri source, double volume, CancellationToken cancellationToken) =>
        Task.FromException(new AudioUnavailableException("Audio playback is not configured or supported on this target."));
    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken) =>
        Task.FromException(new AudioUnavailableException("Audio playback is not configured or supported on this target."));
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
