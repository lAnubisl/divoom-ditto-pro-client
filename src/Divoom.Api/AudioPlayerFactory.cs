using Divoom;

namespace Divoom.Api;

internal static class AudioPlayerFactory
{
    public static IDivoomAudioPlayer Create(IConfiguration configuration, ILogger<DivoomAudioPlayer>? logger)
    {
        var device = configuration["DIVOOM_AUDIO_DEVICE"];
        if (string.IsNullOrWhiteSpace(device)) return new UnavailableAudioPlayer("audio_not_configured");
        var enabled = configuration["DIVOOM_AUDIO_AUTO_RECONNECT"];
        var recovery = new AudioRecoveryOptions { Enabled = enabled is null || bool.Parse(enabled) };
        Action<AudioPlaybackStatus> report = status => logger?.LogInformation(
            "AUDIO_RECOVERY: state={State}; attempts={Attempts}; error={Error}; lastError={LastError}",
            status.State, status.RetryAttempts, status.Error, status.LastError);
#if WINDOWS
        if (OperatingSystem.IsWindows()) return new DivoomAudioPlayer(new WindowsAudioPlaybackBackend(device), recovery: recovery, reportRecovery: report, startupTimeout: TimeSpan.FromSeconds(20));
#endif
        if (OperatingSystem.IsLinux())
        {
            var address = configuration["DIVOOM_AUDIO_BLUETOOTH_ADDRESS"];
            var bluetooth = string.IsNullOrWhiteSpace(address) ? null : new LinuxBluetoothAudioConnector(address,
                configuration["DIVOOM_ADAPTER"] ?? "hci0", configuration["DBUS_SYSTEM_BUS_ADDRESS"] ?? "unix:path=/run/dbus/system_bus_socket");
            return new DivoomAudioPlayer(new LinuxAudioPlaybackBackend(device, bluetooth), recovery: recovery, reportRecovery: report, startupTimeout: TimeSpan.FromSeconds(20));
        }
        return new UnavailableAudioPlayer("platform_unsupported");
    }
}
