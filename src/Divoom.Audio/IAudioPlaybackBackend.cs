namespace Divoom;

public interface IAudioPlaybackBackend
{
    /// <summary>Prepare a session on the configured output without starting playback.</summary>
    Task<IAudioPlaybackSession> OpenAsync(Uri source, double volume, CancellationToken cancellationToken = default);
}
