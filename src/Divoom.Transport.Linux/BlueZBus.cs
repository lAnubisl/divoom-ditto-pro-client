using Tmds.DBus;

namespace Divoom;

internal sealed class BlueZBus(string address) : IBlueZBus
{
    private readonly Connection connection = new(address);
    private const string Destination = "org.bluez";
    public Task OpenAsync(CancellationToken token) => connection.ConnectAsync().WaitAsync(token);

    public async Task<IDictionary<string, IDictionary<string, IDictionary<string, object>>>> GetObjectsAsync(CancellationToken token)
    {
        var objects = await connection.CreateProxy<BlueZ.IObjectManager>(Destination, "/").GetManagedObjectsAsync().WaitAsync(token).ConfigureAwait(false);
        return objects.ToDictionary(p => p.Key.ToString(), p => p.Value);
    }

    public Task<IDictionary<string, object>> GetPropertiesAsync(string path, string iface, CancellationToken token) =>
        connection.CreateProxy<BlueZ.IProperties>(Destination, path).GetAllAsync(iface).WaitAsync(token);

    public Task CallAsync(string path, string iface, string method, CancellationToken token)
    {
        Task call = (iface, method) switch
        {
            (BlueZInterfaces.Adapter, "StartDiscovery") => connection.CreateProxy<BlueZ.IAdapter>(Destination, path).StartDiscoveryAsync(),
            (BlueZInterfaces.Adapter, "StopDiscovery") => connection.CreateProxy<BlueZ.IAdapter>(Destination, path).StopDiscoveryAsync(),
            (BlueZInterfaces.Device, "Connect") => connection.CreateProxy<BlueZ.IDevice>(Destination, path).ConnectAsync(),
            (BlueZInterfaces.Device, "Disconnect") => connection.CreateProxy<BlueZ.IDevice>(Destination, path).DisconnectAsync(),
            (BlueZInterfaces.Characteristic, "StartNotify") => connection.CreateProxy<BlueZ.ICharacteristic>(Destination, path).StartNotifyAsync(),
            (BlueZInterfaces.Characteristic, "StopNotify") => connection.CreateProxy<BlueZ.ICharacteristic>(Destination, path).StopNotifyAsync(),
            _ => throw new ArgumentException("Unsupported BlueZ method.", nameof(method))
        };
        return call.WaitAsync(token);
    }

    public Task WriteAsync(string path, byte[] bytes, CancellationToken token) =>
        connection.CreateProxy<BlueZ.ICharacteristic>(Destination, path)
            .WriteValueAsync(bytes, new Dictionary<string, object> { ["type"] = "request" }).WaitAsync(token);

    public async Task<IDisposable> WatchAsync(string path, Action<BlueZPropertyChange> handler, Action<Exception> onError, CancellationToken token)
    {
        var pending = connection.CreateProxy<BlueZ.IProperties>(Destination, path)
            .WatchPropertiesChangedAsync(p => handler(new(p.Interface, p.Changed, p.Invalidated)), onError);
        try { return await pending.WaitAsync(token).ConfigureAwait(false); }
        catch
        {
            // Cancellation may win while AddMatch is still pending. Dispose any late subscription.
            _ = pending.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); else _ = t.Exception; }, TaskScheduler.Default);
            throw;
        }
    }
    public void Dispose() => connection.Dispose();
}
