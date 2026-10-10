namespace Divoom;

/// <summary>A host-owned player for the device's audio endpoint, independent of its BLE display.</summary>
public interface IDivoomAudioPlayer : IAsyncDisposable
{
    AudioPlaybackStatus Status { get; }
    Task StartAsync(Uri source, double volume = 0.25, CancellationToken cancellationToken = default);
    Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
