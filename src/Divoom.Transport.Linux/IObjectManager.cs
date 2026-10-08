using Tmds.DBus;

namespace Divoom.BlueZ;

// Public because Tmds.DBus generates proxies in a separate dynamic assembly.
// These describe the BlueZ wire API; applications use BlueZBleTransport.
[DBusInterface("org.freedesktop.DBus.ObjectManager")]
public interface IObjectManager : IDBusObject
{
    Task<IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>> GetManagedObjectsAsync();
}
