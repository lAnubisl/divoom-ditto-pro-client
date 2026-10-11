namespace Divoom;

public interface IAudioPlaybackBackend
{
    /// <summary>Prepare a session on the configured output without starting playback.</summary>
    Task<IAudioPlaybackSession> OpenAsync(Uri source, double volume, CancellationToken cancellationToken);
    /// <summary>Attempt recovery of the configured output before reopening a failed stream.</summary>
    Task PrepareRecoveryAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
