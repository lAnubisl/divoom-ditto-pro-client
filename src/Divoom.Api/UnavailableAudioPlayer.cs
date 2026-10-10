using Divoom;

namespace Divoom.Api;

internal sealed class UnavailableAudioPlayer(string reason) : IDivoomAudioPlayer
{
    public AudioPlaybackStatus Status { get; } = new("unavailable", 0.25, reason);
    public Task StartAsync(Uri source, double volume = 0.25, CancellationToken cancellationToken = default) =>
        Task.FromException(new AudioUnavailableException("Audio playback is not configured or supported on this target."));
    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        Task.FromException(new AudioUnavailableException("Audio playback is not configured or supported on this target."));
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
