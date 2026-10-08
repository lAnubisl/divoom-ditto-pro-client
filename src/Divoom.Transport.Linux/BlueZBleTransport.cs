using System.Runtime.ExceptionServices;
using Tmds.DBus;

namespace Divoom;

/// <summary>Linux BlueZ GATT UART transport over the host system D-Bus. No Divoom protocol commands are sent by this class.</summary>
public sealed class BlueZBleTransport : IDittoProTransport
{
    private readonly string address;
    private readonly BlueZTransportOptions options;
    private readonly Func<IBlueZBus> createBus;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object stateLock = new();
    private IBlueZBus? bus;
    private string? adapterPath, devicePath, writePath, notifyPath;
    private IDisposable? deviceWatch, notifyWatch;
    private bool discoveryStarted, notifyStarted, disposed;
    private volatile bool connected;
    private volatile int maxWriteSize;
    private int generation, stateVersion;

    public BlueZBleTransport(string bluetoothAddress, BlueZTransportOptions? options = null)
        : this(bluetoothAddress, options ?? new(), null) { }

    internal BlueZBleTransport(string bluetoothAddress, BlueZTransportOptions options, Func<IBlueZBus>? createBus)
    {
        ArgumentNullException.ThrowIfNull(bluetoothAddress);
        ArgumentNullException.ThrowIfNull(options);
        var mac = bluetoothAddress.Replace(":", "").Replace("-", "");
        if (mac.Length != 12 || !ulong.TryParse(mac, System.Globalization.NumberStyles.HexNumber, null, out var value) || value == 0)
            throw new ArgumentException("Expected a six-byte Bluetooth MAC address.", nameof(bluetoothAddress));
        address = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2))).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(options.BusAddress) || string.IsNullOrWhiteSpace(options.Adapter) ||
            !System.Text.RegularExpressions.Regex.IsMatch(options.Adapter, "^(hci[0-9]+|(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2})$") ||
            options.DiscoveryTimeout <= TimeSpan.Zero || options.ConnectionTimeout <= TimeSpan.Zero)
            throw new ArgumentException("Invalid BlueZ transport options.", nameof(options));
        this.options = options;
        this.createBus = createBus ?? (() => new BlueZBus(options.BusAddress));
    }

    public bool IsConnected => connected;
    public int MaxWriteSize => connected ? maxWriteSize : 0;
    public event Action<ReadOnlyMemory<byte>>? NotificationReceived;
    private string AdapterPath => adapterPath ?? throw new InvalidOperationException("BlueZ adapter has not been resolved.");

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (connected) return;
            using (var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                await DisconnectCoreAsync(cleanup.Token).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.ConnectionTimeout);
            var token = timeout.Token;
            try
            {
                bus = createBus();
                await bus.OpenAsync(token).ConfigureAwait(false);
                devicePath = await FindDeviceAsync(token).ConfigureAwait(false);
                var session = generation;
                deviceWatch = await bus.WatchAsync(devicePath, change => DeviceChanged(session, change),
                    _ => MarkDisconnected(session), token).ConfigureAwait(false);
                var device = await bus.GetPropertiesAsync(devicePath, BlueZInterfaces.Device, token).ConfigureAwait(false);
                if (!IsTrue(device, "Connected"))
                    await bus.CallAsync(devicePath, BlueZInterfaces.Device, "Connect", token).ConfigureAwait(false);
                await WaitForServicesAsync(token).ConfigureAwait(false);
                var objects = await bus.GetObjectsAsync(token).ConfigureAwait(false);
                var services = objects.Where(p => p.Value.TryGetValue(BlueZInterfaces.Service, out var properties) &&
                    SamePath(properties, "Device", devicePath) && UuidIs(properties, DittoProUart.Service)).Select(p => p.Key).ToArray();
                if (services.Length != 1) throw new IOException($"Expected one Divoom UART service; found {services.Length}.");
                var service = services[0];
                var chars = objects.Where(p => p.Value.TryGetValue(BlueZInterfaces.Characteristic, out var properties) && SamePath(properties, "Service", service)).ToArray();
                writePath = FindCharacteristic(chars, DittoProUart.Write);
                notifyPath = FindCharacteristic(chars, DittoProUart.Notify);
                var write = objects[writePath][BlueZInterfaces.Characteristic];
                var notify = objects[notifyPath][BlueZInterfaces.Characteristic];
                if (!HasFlag(write, "write")) throw new IOException("UART characteristic must support write-with-response for reliable upload.");
                if (!HasFlag(notify, "notify") && !HasFlag(notify, "indicate")) throw new IOException("UART characteristic does not support notifications or indications.");
                maxWriteSize = write.TryGetValue("MTU", out var mtu) && mtu is ushort size && size >= 23 ? size - 3 : 20;
                notifyWatch = await bus.WatchAsync(notifyPath, change => NotificationChanged(session, change),
                    _ => MarkDisconnected(session), token).ConfigureAwait(false);
                // Track attempted calls too: cancellation can arrive before the D-Bus reply.
                notifyStarted = true;
                await bus.CallAsync(notifyPath, BlueZInterfaces.Characteristic, "StartNotify", token).ConfigureAwait(false);
                // Do not overwrite a newer disconnect signal with a stale property snapshot.
                while (true)
                {
                    int version;
                    lock (stateLock) version = stateVersion;
                    var state = await bus.GetPropertiesAsync(devicePath, BlueZInterfaces.Device, token).ConfigureAwait(false);
                    lock (stateLock)
                    {
                        if (version != stateVersion) continue;
                        if (!IsTrue(state, "Connected") || !IsTrue(state, "ServicesResolved")) throw new IOException("Device disconnected during GATT setup.");
                        connected = true;
                        break;
                    }
                }
            }
            catch (Exception error)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await DisconnectCoreAsync(cleanup.Token).ConfigureAwait(false); } catch { /* Preserve connection failure. */ }
                if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                    throw new TimeoutException($"BlueZ connection to {address} timed out.", error);
                throw;
            }
        }
        finally { operations.Release(); }
    }

    private async Task<string> FindDeviceAsync(CancellationToken token)
    {
        var objects = await bus!.GetObjectsAsync(token).ConfigureAwait(false);
        adapterPath = options.Adapter.StartsWith("hci", StringComparison.Ordinal)
            ? $"/org/bluez/{options.Adapter}"
            : objects.Where(p => p.Value.TryGetValue(BlueZInterfaces.Adapter, out var properties) &&
                properties.TryGetValue("Address", out var value) &&
                string.Equals(value as string, options.Adapter, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Key).SingleOrDefault()
                ?? throw new IOException($"BlueZ adapter {options.Adapter} was not found.");
        if (!objects.TryGetValue(AdapterPath, out var interfaces) || !interfaces.TryGetValue(BlueZInterfaces.Adapter, out var adapter))
            throw new IOException($"BlueZ adapter {options.Adapter} was not found.");
        if (!IsTrue(adapter, "Powered")) throw new IOException($"BlueZ adapter {options.Adapter} is powered off.");
        string? Find(IDictionary<string, IDictionary<string, IDictionary<string, object>>> snapshot) => snapshot
            .Where(p => p.Key.StartsWith(AdapterPath + "/", StringComparison.Ordinal) && p.Value.TryGetValue(BlueZInterfaces.Device, out var properties) &&
                properties.TryGetValue("Address", out var mac) && string.Equals(mac as string, address, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Key).SingleOrDefault();
        var found = Find(objects);
        if (found is not null) return found;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(options.DiscoveryTimeout);
        discoveryStarted = true;
        await bus.CallAsync(AdapterPath, BlueZInterfaces.Adapter, "StartDiscovery", timeout.Token).ConfigureAwait(false);
        while (found is null)
        {
            await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            found = Find(await bus.GetObjectsAsync(timeout.Token).ConfigureAwait(false));
        }
        devicePath = found;
        await bus.CallAsync(AdapterPath, BlueZInterfaces.Adapter, "StopDiscovery", token).ConfigureAwait(false);
        discoveryStarted = false;
        return found;
    }

    private async Task WaitForServicesAsync(CancellationToken token)
    {
        while (true)
        {
            var state = await bus!.GetPropertiesAsync(devicePath!, BlueZInterfaces.Device, token).ConfigureAwait(false);
            if (IsTrue(state, "Connected") && IsTrue(state, "ServicesResolved")) return;
            await Task.Delay(100, token).ConfigureAwait(false);
        }
    }

    private static string FindCharacteristic(KeyValuePair<string, IDictionary<string, IDictionary<string, object>>>[] chars, Guid uuid)
    {
        var paths = chars.Where(p => UuidIs(p.Value[BlueZInterfaces.Characteristic], uuid)).Select(p => p.Key).ToArray();
        return paths.Length == 1 ? paths[0] : throw new IOException($"Expected one UART characteristic {uuid}; found {paths.Length}.");
    }
    private static bool UuidIs(IDictionary<string, object> properties, Guid uuid) => properties.TryGetValue("UUID", out var value) && Guid.TryParse(value as string, out var actual) && actual == uuid;
    private static bool IsTrue(IDictionary<string, object> properties, string name) => properties.TryGetValue(name, out var value) && value is true;
    private static bool SamePath(IDictionary<string, object> properties, string name, string path) => properties.TryGetValue(name, out var value) && value.ToString() == path;
    private static bool HasFlag(IDictionary<string, object> properties, string flag) => properties.TryGetValue("Flags", out var value) && value is string[] flags && flags.Contains(flag);

    private void MarkDisconnected(int session)
    {
        lock (stateLock) { if (session == generation) { stateVersion++; connected = false; } }
    }
    private void DeviceChanged(int session, BlueZPropertyChange change)
    {
        if (change.Interface != BlueZInterfaces.Device) return;
        lock (stateLock)
        {
            if (session != generation) return;
            stateVersion++;
            if (change.Invalidated.Contains("Connected") || change.Invalidated.Contains("ServicesResolved") ||
                change.Changed.TryGetValue("Connected", out var c) && c is false ||
                change.Changed.TryGetValue("ServicesResolved", out var s) && s is false) connected = false;
        }
    }
    private void NotificationChanged(int session, BlueZPropertyChange change)
    {
        if (change.Interface != BlueZInterfaces.Characteristic || !change.Changed.TryGetValue("Value", out var value) || value is not byte[] bytes) return;
        lock (stateLock) { if (session != generation) return; }
        NotificationReceived?.Invoke(bytes.ToArray());
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!connected || bus is null || writePath is null) throw new IOException("BlueZ BLE transport is disconnected.");
            if (data.IsEmpty || data.Length > MaxWriteSize) throw new ArgumentOutOfRangeException(nameof(data));
            try { await bus.WriteAsync(writePath, data.ToArray(), cancellationToken).ConfigureAwait(false); }
            catch { connected = false; throw; }
        }
        finally { operations.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { operations.Release(); }
    }

    private async Task DisconnectCoreAsync(CancellationToken token)
    {
        lock (stateLock) { generation++; stateVersion++; connected = false; maxWriteSize = 0; }
        Exception? failure = null;
        void Release(IDisposable? resource)
        {
            try { resource?.Dispose(); }
            catch (Exception error) { failure ??= error; }
        }
        Release(deviceWatch); Release(notifyWatch); deviceWatch = null; notifyWatch = null;
        if (bus is null)
        {
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            return;
        }
        async Task Stop(string path, string iface, string method)
        {
            try { await bus.CallAsync(path, iface, method, token).ConfigureAwait(false); }
            catch (DBusException e) when (e.ErrorName is "org.bluez.Error.NotConnected" or "org.freedesktop.DBus.Error.UnknownObject" or "org.bluez.Error.DoesNotExist") { }
            catch (Exception error) { failure ??= error; }
        }
        try
        {
            if (notifyStarted && notifyPath is not null) await Stop(notifyPath, BlueZInterfaces.Characteristic, "StopNotify").ConfigureAwait(false);
            if (devicePath is not null) await Stop(devicePath, BlueZInterfaces.Device, "Disconnect").ConfigureAwait(false);
            if (discoveryStarted) await Stop(AdapterPath, BlueZInterfaces.Adapter, "StopDiscovery").ConfigureAwait(false);
        }
        finally
        {
            Release(bus); bus = null; adapterPath = devicePath = writePath = notifyPath = null;
            discoveryStarted = notifyStarted = false;
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public async ValueTask DisposeAsync()
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await DisconnectCoreAsync(cleanup.Token).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }
}
