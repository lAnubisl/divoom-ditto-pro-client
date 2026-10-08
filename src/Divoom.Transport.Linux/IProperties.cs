using Tmds.DBus;

namespace Divoom.BlueZ;

// Public because Tmds.DBus generates proxies in a separate dynamic assembly.
// These describe the BlueZ wire API; applications use BlueZBleTransport.
[DBusInterface("org.freedesktop.DBus.Properties", GetAllPropertiesMethod = "")]
public interface IProperties : IDBusObject
{
    Task<IDictionary<string, object>> GetAllAsync(string iface);
    Task<IDisposable> WatchPropertiesChangedAsync(Action<(string Interface, IDictionary<string, object> Changed, string[] Invalidated)> handler, Action<Exception> onError);
}
