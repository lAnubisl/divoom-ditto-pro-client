namespace Divoom;

/// <summary>Plays through an exact PulseAudio sink (also supported by PipeWire's Pulse server).</summary>
public sealed class LinuxAudioPlaybackBackend(string sink, LinuxBluetoothAudioConnector? bluetooth) : IAudioPlaybackBackend
{
    public Task PrepareRecoveryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux()) throw new AudioUnavailableException("Linux audio requires Linux.");
        return bluetooth?.ConnectAsync(cancellationToken) ?? Task.CompletedTask;
    }
    public async Task<IAudioPlaybackSession> OpenAsync(Uri source, double volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sink) || sink is "auto" or "default" || sink.StartsWith('@'))
            throw new AudioUnavailableException("An explicit PulseAudio sink name is required.");
        if (!OperatingSystem.IsLinux()) throw new AudioUnavailableException("Linux audio requires Linux.");
        try
        {
            return await LinuxAudioPlaybackSession.OpenAsync(sink, source, volume, cancellationToken, waitForSink: bluetooth is not null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (AudioUnavailableException) { throw; }
        catch (Exception error) { throw new AudioUnavailableException("Cannot open Linux audio. Check mpv, the Pulse server and the selected sink.", error); }
    }
}
