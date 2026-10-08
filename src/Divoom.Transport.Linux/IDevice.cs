using Tmds.DBus;

namespace Divoom.BlueZ;

// Public because Tmds.DBus generates proxies in a separate dynamic assembly.
// These describe the BlueZ wire API; applications use BlueZBleTransport.
[DBusInterface("org.bluez.Device1")]
public interface IDevice : IDBusObject
{
    Task ConnectAsync();
    Task DisconnectAsync();
}
