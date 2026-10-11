using System.Text.RegularExpressions;
using Tmds.DBus;

namespace Divoom;

/// <summary>Reconnects only an already paired device on the explicitly selected host adapter.</summary>
public sealed class LinuxBluetoothAudioConnector
{
    private readonly string address;
    private readonly string adapter;
    private readonly string busAddress;

    public LinuxBluetoothAudioConnector(string address, string adapter, string busAddress)
    {
        this.address = Normalize(address);
        this.adapter = Regex.IsMatch(adapter, "^hci[0-9]+$") ? adapter : Normalize(adapter);
        this.busAddress = busAddress;
    }

    internal async Task ConnectAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        token = deadline.Token;
        using var connection = new Connection(busAddress);
        await connection.ConnectAsync().WaitAsync(token);
        var objects = await connection.CreateProxy<BlueZ.IObjectManager>("org.bluez", "/").GetManagedObjectsAsync().WaitAsync(token);
        var adapters = objects.Where(p => p.Value.TryGetValue(BlueZInterfaces.Adapter, out var properties) &&
            (p.Key.ToString() == "/org/bluez/" + adapter ||
             properties.TryGetValue("Address", out var value) && value is string mac && string.Equals(mac.Replace(":", ""), adapter, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (adapters.Length != 1) throw new AudioUnavailableException("The configured Bluetooth audio adapter is unavailable.");
        var devices = objects.Where(p => p.Key.ToString().StartsWith(adapters[0].Key + "/dev_", StringComparison.Ordinal) &&
            p.Value.TryGetValue(BlueZInterfaces.Device, out var properties) &&
            properties.TryGetValue("Address", out var value) && value is string mac &&
            string.Equals(mac.Replace(":", ""), address, StringComparison.OrdinalIgnoreCase) &&
            properties.TryGetValue("Paired", out var paired) && paired is true).ToArray();
        if (devices.Length != 1) throw new AudioUnavailableException("The configured Bluetooth audio device must already be paired on this adapter.");
        try
        {
            await connection.CreateProxy<BlueZ.IDevice>("org.bluez", devices[0].Key).ConnectProfileAsync(BluetoothServiceUuids.AudioSink).WaitAsync(token);
        }
        catch (DBusException error) when (error.ErrorName is "org.bluez.Error.AlreadyConnected" or "org.bluez.Error.InProgress")
        {
            // PipeWire may still be publishing the sink, or another client is connecting it.
        }
    }

    private static string Normalize(string value)
    {
        var normalized = value.Replace(":", "").Replace("-", "").ToUpperInvariant();
        if (!Regex.IsMatch(normalized, "^[0-9A-F]{12}$")) throw new ArgumentException("A 48-bit Bluetooth MAC address is required.");
        return normalized;
    }
}
