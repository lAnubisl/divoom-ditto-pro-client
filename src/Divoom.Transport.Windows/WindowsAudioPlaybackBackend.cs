using Windows.Devices.Enumeration;

namespace Divoom;

/// <summary>Uses an explicitly selected, OS-paired audio endpoint; never falls back to the default output.</summary>
public sealed class WindowsAudioPlaybackBackend(string device) : IAudioPlaybackBackend
{
    public async Task<IAudioPlaybackSession> OpenAsync(Uri source, double volume, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new AudioUnavailableException("Windows audio requires Windows.");
        var outputs = await DeviceInformation.FindAllAsync(DeviceClass.AudioRender).AsTask(cancellationToken);
        var matches = outputs.Where(d => d.IsEnabled &&
            (string.Equals(d.Id, device, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(d.Name, device, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length != 1)
            throw new AudioUnavailableException("The configured audio output is absent, disabled, or ambiguous. Pair and connect it in Windows settings.");
        cancellationToken.ThrowIfCancellationRequested();
        try { return new WindowsAudioPlaybackSession(matches[0], source, volume); }
        catch (Exception error) { throw new AudioUnavailableException("Could not open the configured Windows audio output.", error); }
    }
}
