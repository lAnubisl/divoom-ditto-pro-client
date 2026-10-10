using Divoom;

namespace Divoom.Api;

internal static class AudioPlayerFactory
{
    public static IDivoomAudioPlayer Create(IConfiguration configuration)
    {
        var device = configuration["DIVOOM_AUDIO_DEVICE"];
        if (string.IsNullOrWhiteSpace(device)) return new UnavailableAudioPlayer("audio_not_configured");
#if WINDOWS
        if (OperatingSystem.IsWindows()) return new DivoomAudioPlayer(new WindowsAudioPlaybackBackend(device));
#endif
        if (OperatingSystem.IsLinux()) return new DivoomAudioPlayer(new LinuxAudioPlaybackBackend(device));
        return new UnavailableAudioPlayer("platform_unsupported");
    }
}
