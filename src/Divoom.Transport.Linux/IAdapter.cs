using Tmds.DBus;

namespace Divoom.BlueZ;

// Public because Tmds.DBus generates proxies in a separate dynamic assembly.
// These describe the BlueZ wire API; applications use BlueZBleTransport.
[DBusInterface("org.bluez.Adapter1")]
public interface IAdapter : IDBusObject
{
    Task StartDiscoveryAsync();
    Task StopDiscoveryAsync();
}
