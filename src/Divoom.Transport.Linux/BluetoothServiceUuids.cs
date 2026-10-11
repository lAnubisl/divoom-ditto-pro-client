namespace Divoom;

internal static class BluetoothServiceUuids
{
    /// <summary>
    /// Standard Bluetooth Audio Sink service UUID: a remote device that receives
    /// an A2DP audio stream, such as the DitooPro-Audio speaker.
    /// </summary>
    /// <remarks>
    /// Bluetooth SIG assigns the 16-bit service class identifier 0x110B to Audio Sink.
    /// Expanding it with the Bluetooth base UUID
    /// (00000000-0000-1000-8000-00805f9b34fb) produces this 128-bit UUID.
    /// Source: https://www.bluetooth.com/specifications/assigned-numbers/
    /// This is a shared service identifier, not a device MAC address or a Divoom-specific
    /// BLE GATT service for display commands. BlueZ Device1.ConnectProfile expects the
    /// remote service UUID; using Audio Sink requests the speaker's audio connection
    /// during Linux playback recovery without requesting every available profile.
    /// The target device and host adapter are selected separately by configuration.
    /// A successful connection does not by itself confirm audible playback.
    /// </remarks>
    public const string AudioSink = "0000110b-0000-1000-8000-00805f9b34fb";
}
