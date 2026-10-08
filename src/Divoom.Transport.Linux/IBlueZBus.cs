namespace Divoom;

internal interface IBlueZBus : IDisposable
{
    Task OpenAsync(CancellationToken token);
    Task<IDictionary<string, IDictionary<string, IDictionary<string, object>>>> GetObjectsAsync(CancellationToken token);
    Task<IDictionary<string, object>> GetPropertiesAsync(string path, string iface, CancellationToken token);
    Task CallAsync(string path, string iface, string method, CancellationToken token);
    Task WriteAsync(string path, byte[] bytes, CancellationToken token);
    Task<IDisposable> WatchAsync(string path, Action<BlueZPropertyChange> handler, Action<Exception> onError, CancellationToken token);
}
