using Divoom;
using Divoom.BlueZ;
using Tmds.DBus;

internal sealed class TestObjects : IObjectManager
{
    internal const string DevicePath = "/org/bluez/hci0/dev_B1_21_81_4B_E6_42";
    internal const string ServicePath = DevicePath + "/service0010";
    internal const string WritePath = ServicePath + "/char0011";
    internal const string NotifyPath = ServicePath + "/char0012";

    public ObjectPath ObjectPath => new("/");
    public IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>> Objects { get; } = new Dictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>
    {
        [new("/org/bluez/hci0")] = Interfaces(BlueZInterfaces.Adapter, new() { ["Powered"] = true }),
        [new(DevicePath)] = Interfaces(BlueZInterfaces.Device, new() { ["Address"] = "B1:21:81:4B:E6:42", ["Connected"] = false, ["ServicesResolved"] = false }),
        [new(ServicePath)] = Interfaces(BlueZInterfaces.Service, new() { ["UUID"] = DittoProUart.Service.ToString(), ["Device"] = new ObjectPath(DevicePath) }),
        [new(WritePath)] = Interfaces(BlueZInterfaces.Characteristic, new() { ["UUID"] = DittoProUart.Write.ToString(), ["Service"] = new ObjectPath(ServicePath), ["Flags"] = new[] { "write" } }),
        [new(NotifyPath)] = Interfaces(BlueZInterfaces.Characteristic, new() { ["UUID"] = DittoProUart.Notify.ToString(), ["Service"] = new ObjectPath(ServicePath), ["Flags"] = new[] { "notify" } })
    };
    private static IDictionary<string, IDictionary<string, object>> Interfaces(string iface, Dictionary<string, object> props) => new Dictionary<string, IDictionary<string, object>> { [iface] = props };
    public Task<IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>> GetManagedObjectsAsync() => Task.FromResult(Objects);
}
