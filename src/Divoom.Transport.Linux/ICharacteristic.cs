using Tmds.DBus;

namespace Divoom.BlueZ;

// Public because Tmds.DBus generates proxies in a separate dynamic assembly.
// These describe the BlueZ wire API; applications use BlueZBleTransport.
[DBusInterface("org.bluez.GattCharacteristic1")]
public interface ICharacteristic : IDBusObject
{
    Task WriteValueAsync(byte[] bytes, IDictionary<string, object> options);
    Task StartNotifyAsync();
    Task StopNotifyAsync();
}
