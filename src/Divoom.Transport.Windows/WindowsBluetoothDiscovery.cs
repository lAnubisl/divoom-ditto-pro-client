using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace Divoom;

/// <summary>Windows endpoint discovery. Does not initialize a Divoom display session.</summary>
public static class WindowsBluetoothDiscovery
{
    private static readonly string[] Properties =
        ["System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.IsPresent", "System.Devices.Aep.ProtocolId"];

    public static async Task<IReadOnlyList<WindowsBluetoothEndpoint>> ScanAsync(TimeSpan duration, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken)
            ?? throw new IOException("No Bluetooth adapter found.");
        var radio = await adapter.GetRadioAsync().AsTask(cancellationToken);
        log?.Invoke($"Adapter: {radio.Name}; state={radio.State}; classic={adapter.IsClassicSupported}; BLE={adapter.IsLowEnergySupported}");
        if (radio.State != RadioState.On) throw new IOException("Bluetooth radio is not on.");
        var devices = new ConcurrentDictionary<string, DeviceInformation>();
        const string selector = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\") OR (System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";
        var watcher = DeviceInformation.CreateWatcher(selector, Properties, DeviceInformationKind.AssociationEndpoint);
        watcher.Added += (_, device) => devices[device.Id] = device;
        watcher.Updated += (_, update) => { if (devices.TryGetValue(update.Id, out var device)) device.Update(update); };
        watcher.Removed += (sender, update) => devices.TryRemove(update.Id, out _);
        watcher.Start();
        try { await Task.Delay(duration, cancellationToken); }
        finally { watcher.Stop(); }
        var endpoints = new List<WindowsBluetoothEndpoint>();
        foreach (var device in devices.Values)
        {
            var address = device.Properties.GetValueOrDefault(Properties[0])?.ToString()?.Replace(":", "").Replace("-", "");
            if (!ulong.TryParse(address, System.Globalization.NumberStyles.HexNumber, null, out var value)) continue;
            var ble = device.Properties.GetValueOrDefault(Properties[3])?.ToString()?.Contains("bb7bb05e", StringComparison.OrdinalIgnoreCase) == true;
            var endpoint = new WindowsBluetoothEndpoint(device.Name, value, ble, device.Pairing.IsPaired);
            endpoints.Add(endpoint);
            log?.Invoke($"Found: {endpoint.Name}; address={endpoint.Address:X12}; paired={endpoint.Paired}; BLE={endpoint.IsBle}");
        }
        return endpoints;
    }

    public static async Task<IReadOnlyList<WindowsBluetoothEndpoint>> ScanClassicAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Native inquiry is synchronous; cancellation is checked again after it returns.
        var devices = await Task.Run(ClassicDiscovery.Scan, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return devices.Select(d => new WindowsBluetoothEndpoint(d.Name, d.Address, false, d.Paired)).ToArray();
    }
}
