using Divoom;
using Tmds.DBus;
using NUnit.Framework;

public sealed class BluetoothAudioRecoveryTests
{
    [Test]
    public async Task ConnectsOnlyPairedAudioProfileOnSelectedAdapterOverActualDbus()
    {
        var server = new ServerConnectionOptions();
        using var connection = new Connection(server);
        var root = new TestObjects();
        var properties = root.Objects[TestObjects.DevicePath][BlueZInterfaces.Device];
        properties["Paired"] = true;
        root.Objects[new("/org/bluez/hci0")][BlueZInterfaces.Adapter]["Address"] = "00:11:22:33:44:55";
        var device = new TestEndpoint(TestObjects.DevicePath, BlueZInterfaces.Device, properties);
        await connection.RegisterObjectAsync(root);
        await connection.RegisterObjectAsync(device);
        var bus = await server.StartAsync("tcp:host=127.0.0.1,port=0");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connector = new LinuxBluetoothAudioConnector("B1:21:81:4B:E6:42", "00:11:22:33:44:55", bus);
        await connector.ConnectAsync(deadline.Token);
        Assert.That(device.ConnectedProfile, Is.EqualTo("0000110b-0000-1000-8000-00805f9b34fb"));
        Assert.That(device.ConnectCount, Is.Zero, "Do not connect other Bluetooth profiles");
        Assert.That(device.DisconnectCount, Is.Zero, "Do not disturb the BLE connection");
        device.ConnectedProfile = null;
        properties["Paired"] = false;
        Assert.ThrowsAsync<AudioUnavailableException>(() => connector.ConnectAsync(deadline.Token));
        Assert.That(device.ConnectedProfile, Is.Null);
        properties["Paired"] = true;
        var wrongAdapter = new LinuxBluetoothAudioConnector("B1:21:81:4B:E6:42", "hci1", bus);
        Assert.ThrowsAsync<AudioUnavailableException>(() => wrongAdapter.ConnectAsync(deadline.Token));
        Assert.That(device.ConnectedProfile, Is.Null);
        root.Objects.Remove(new("/org/bluez/hci0"));
        Assert.ThrowsAsync<AudioUnavailableException>(() => connector.ConnectAsync(deadline.Token));
        root.Objects[new("/org/bluez/hci0")] = new Dictionary<string, IDictionary<string, object>>
        { [BlueZInterfaces.Adapter] = new Dictionary<string, object> { ["Address"] = "00:11:22:33:44:55" } };
        await connector.ConnectAsync(deadline.Token);
        Assert.That(device.ConnectedProfile, Is.Not.Null, "Retry resolves adapter objects again after USB return");
    }

    [TestCase("invalid", "hci0")]
    [TestCase("B1:21:81:4B:E6:42", "../hci0")]
    public void RejectsInvalidAddresses(string address, string adapter) =>
        Assert.Throws<ArgumentException>(() => new LinuxBluetoothAudioConnector(address, adapter, "tcp:host=127.0.0.1,port=0"));
}
