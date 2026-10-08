using Divoom;
using Divoom.BlueZ;
using Tmds.DBus;

internal sealed class TestEndpoint(string path, string iface, IDictionary<string, object> properties) : IDevice, ICharacteristic, IProperties, IAdapter
{
    public ObjectPath ObjectPath => new(path);
    private event Action<(string Interface, IDictionary<string, object> Changed, string[] Invalidated)>? changed;
    public int ConnectCount, DisconnectCount, StartCount, StopCount;
    public int DiscoveryStarts, DiscoveryStops;
    public IDictionary<string, object> Properties => properties;
    public Action? OnDiscovery;
    public Func<Task>? OnStart;
    public Action? OnStop;
    public bool ResolveServices = true;
    public string? WriteType;
    public List<byte[]> Writes { get; } = [];
    public Task ConnectAsync() { ConnectCount++; properties["Connected"] = true; properties["ServicesResolved"] = ResolveServices; return Task.CompletedTask; }
    public Task DisconnectAsync() { DisconnectCount++; properties["Connected"] = false; properties["ServicesResolved"] = false; return Task.CompletedTask; }
    public Task StartNotifyAsync() { StartCount++; return OnStart?.Invoke() ?? Task.CompletedTask; }
    public Task StopNotifyAsync() { StopCount++; OnStop?.Invoke(); return Task.CompletedTask; }
    public Task StartDiscoveryAsync() { DiscoveryStarts++; OnDiscovery?.Invoke(); return Task.CompletedTask; }
    public Task StopDiscoveryAsync() { DiscoveryStops++; return Task.CompletedTask; }
    public Task WriteValueAsync(byte[] bytes, IDictionary<string, object> options) { Writes.Add(bytes.ToArray()); WriteType = (string)options["type"]; return Task.CompletedTask; }
    public Task<IDictionary<string, object>> GetAllAsync(string requestedInterface) => Task.FromResult(properties);
    public void Emit(IDictionary<string, object> values) { foreach (var value in values) properties[value.Key] = value.Value; changed?.Invoke((iface, values, [])); }
    public Task<IDisposable> WatchPropertiesChangedAsync(Action<(string Interface, IDictionary<string, object> Changed, string[] Invalidated)> handler, Action<Exception> onError)
    {
        changed += handler;
        return Task.FromResult<IDisposable>(new Unsubscribe(() => changed -= handler));
    }
}
