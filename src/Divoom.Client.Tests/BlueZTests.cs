using static TestObjects;
using Divoom;
using Divoom.BlueZ;
using Tmds.DBus;
using NUnit.Framework;

public sealed class BlueZTests
{

    [Test]
    public async Task LocalDbusExercisesConnectionDiscoveryAndCleanup()
    {
        // Actual D-Bus messages over a local TCP connection exercise dynamic proxy
        // generation, nested object dictionaries, variant options and notifications.
        var server = new ServerConnectionOptions();
        using var connection = new Connection(server);
        var root = new TestObjects();
        var device = new TestEndpoint(DevicePath, BlueZInterfaces.Device, root.Objects[DevicePath][BlueZInterfaces.Device]);
        var write = new TestEndpoint(WritePath, BlueZInterfaces.Characteristic, root.Objects[WritePath][BlueZInterfaces.Characteristic]);
        var notify = new TestEndpoint(NotifyPath, BlueZInterfaces.Characteristic, root.Objects[NotifyPath][BlueZInterfaces.Characteristic]);
        var adapter = new TestEndpoint("/org/bluez/hci0", BlueZInterfaces.Adapter, root.Objects[new("/org/bluez/hci0")][BlueZInterfaces.Adapter]);
        await connection.RegisterObjectAsync(root);
        await connection.RegisterObjectAsync(device);
        await connection.RegisterObjectAsync(write);
        await connection.RegisterObjectAsync(notify);
        await connection.RegisterObjectAsync(adapter);
        var boundAddress = await server.StartAsync("tcp:host=127.0.0.1,port=0");
        var options = new BlueZTransportOptions { BusAddress = boundAddress, ConnectionTimeout = TimeSpan.FromSeconds(3) };
        var transport = new BlueZBleTransport("B1:21:81:4B:E6:42", options);
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.NotificationReceived += bytes => received.TrySetResult(bytes.ToArray());
        try
        {
            await transport.ConnectAsync();
            Assert.That(transport.IsConnected && transport.MaxWriteSize == 20 && notify.StartCount == 1, Is.True, "BlueZ connection, conservative MTU and StartNotify");
            await transport.ConnectAsync();
            Assert.That(device.ConnectCount == 1 && notify.StartCount == 1, Is.True, "BlueZ connection reused");
            await transport.WriteAsync(new byte[] { 1, 2, 3 });
            Assert.That(write.Writes.Single().SequenceEqual(new byte[] { 1, 2, 3 }) && write.WriteType == "request", Is.True, "WriteValue uses byte array and ATT request option");
            notify.Emit(new Dictionary<string, object> { ["Value"] = new byte[] { 4, 5, 6 } });
            Assert.That((await received.Task.WaitAsync(TimeSpan.FromSeconds(2))).SequenceEqual(new byte[] { 4, 5, 6 }), Is.True, "PropertiesChanged forwards characteristic bytes");
            await Assert.ThatAsync(async () => await transport.WriteAsync(new byte[21]), Throws.InstanceOf<ArgumentOutOfRangeException>());
            await transport.DisconnectAsync();
            Assert.That(!transport.IsConnected && transport.MaxWriteSize == 0 && notify.StopCount == 1 && device.DisconnectCount == 1, Is.True, "StopNotify, device disconnect and state reset");
            await transport.ConnectAsync();
            Assert.That(transport.IsConnected && device.ConnectCount == 2, Is.True, "Reconnect on a new D-Bus session");
            device.Emit(new Dictionary<string, object> { ["Connected"] = false });
            using var lostTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (transport.IsConnected) await Task.Delay(10, lostTimeout.Token);
            await Assert.ThatAsync(async () => await transport.WriteAsync(new byte[] { 1 }), Throws.InstanceOf<IOException>());
            await transport.DisposeAsync();
            await transport.DisposeAsync();
            Assert.That(device.DisconnectCount == 2 && notify.StopCount == 2, Is.True, "BlueZ disposal disconnects once and is idempotent");
            await Assert.ThatAsync(async () => await transport.ConnectAsync(), Throws.InstanceOf<ObjectDisposedException>());
        }
        finally { await transport.DisposeAsync(); }
        write.Properties["MTU"] = (ushort)185;
        await using (var negotiated = new BlueZBleTransport("B121814BE642", options))
        {
            await negotiated.ConnectAsync();
            Assert.That(negotiated.MaxWriteSize == 182, Is.True, "Negotiated MTU minus ATT header");
        }
        write.Properties.Remove("MTU");
        // A USB controller can move from hci1 to hci0 after reboot. Resolve its
        // address instead of assuming its current index; do not use another radio.
        adapter.Properties["Address"] = "00:1A:7D:DA:71:11";
        root.Objects[new("/org/bluez/hci1")] = new Dictionary<string, IDictionary<string, object>>
        {
            [BlueZInterfaces.Adapter] = new Dictionary<string, object>
            {
                ["Address"] = "AA:BB:CC:DD:EE:FF", ["Powered"] = false
            }
        };
        var stableOptions = new BlueZTransportOptions
        {
            BusAddress = boundAddress, Adapter = "00:1a:7d:da:71:11", ConnectionTimeout = TimeSpan.FromSeconds(3)
        };
        await using (var stableAdapter = new BlueZBleTransport("B1:21:81:4B:E6:42", stableOptions))
        {
            await stableAdapter.ConnectAsync();
            Assert.That(stableAdapter.IsConnected, Is.True, "Controller address resolves to its current hci index");
            await stableAdapter.DisconnectAsync();
            await stableAdapter.ConnectAsync();
            Assert.That(stableAdapter.IsConnected, Is.True, "Controller address is resolved again on reconnect");
        }
        var beforeMissingAdapter = device.ConnectCount;
        await using (var missingAdapter = new BlueZBleTransport("B1:21:81:4B:E6:42",
            new BlueZTransportOptions { BusAddress = boundAddress, Adapter = "11:22:33:44:55:66" }))
        {
            await Assert.ThatAsync(async () => await missingAdapter.ConnectAsync(), Throws.InstanceOf<IOException>());
            Assert.That(device.ConnectCount == beforeMissingAdapter, Is.True, "Missing controller does not fall back to another radio");
        }
        root.Objects.Remove(new("/org/bluez/hci1"));
        var failingCleanup = new BlueZBleTransport("B1:21:81:4B:E6:42", options);
        await failingCleanup.ConnectAsync();
        var beforeCleanup = device.DisconnectCount;
        notify.OnStop = () => throw new DBusException("org.bluez.Error.Failed", "Test StopNotify failure");
        await Assert.ThatAsync(async () => await failingCleanup.DisposeAsync(), Throws.InstanceOf<DBusException>());
        Assert.That(!failingCleanup.IsConnected && device.DisconnectCount == beforeCleanup + 1, Is.True, "Notification cleanup failure still disconnects device");
        await failingCleanup.DisposeAsync();
        notify.OnStop = null;

        var deviceObject = root.Objects[new(DevicePath)];
        root.Objects.Remove(new(DevicePath));
        adapter.OnDiscovery = () => root.Objects[new(DevicePath)] = deviceObject;
        await using (var discovered = new BlueZBleTransport("B1:21:81:4B:E6:42", options))
        {
            await discovered.ConnectAsync();
            Assert.That(discovered.IsConnected && adapter.DiscoveryStarts == 1 && adapter.DiscoveryStops == 1, Is.True, "Unknown device is discovered and own discovery session stopped");
        }
        adapter.OnDiscovery = null;
        var connectionCount = device.ConnectCount;
        adapter.Properties["Powered"] = false;
        await using (var disabled = new BlueZBleTransport("B1:21:81:4B:E6:42", options))
        {
            await Assert.ThatAsync(async () => await disabled.ConnectAsync(), Throws.InstanceOf<IOException>());
            Assert.That(!disabled.IsConnected && device.ConnectCount == connectionCount, Is.True, "Powered-off adapter fails before connecting");
        }
        adapter.Properties["Powered"] = true;
        write.Properties["Flags"] = new[] { "write-without-response" };
        var disconnectCount = device.DisconnectCount;
        await using (var unreliable = new BlueZBleTransport("B1:21:81:4B:E6:42", options))
        {
            await Assert.ThatAsync(async () => await unreliable.ConnectAsync(), Throws.InstanceOf<IOException>());
            Assert.That(!unreliable.IsConnected && device.DisconnectCount == disconnectCount + 1, Is.True, "Unsupported GATT flags clean up device connection");
        }
        write.Properties["Flags"] = new[] { "write" };

        var notifyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishNotify = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        notify.OnStart = () => { notifyEntered.TrySetResult(); return finishNotify.Task; };
        var stopCount = notify.StopCount;
        disconnectCount = device.DisconnectCount;
        using (var cancelConnect = new CancellationTokenSource())
        {
            await using var cancelled = new BlueZBleTransport("B1:21:81:4B:E6:42", options);
            var connecting = cancelled.ConnectAsync(cancelConnect.Token);
            await notifyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancelConnect.Cancel();
            await Assert.ThatAsync(async () => await connecting, Throws.InstanceOf<OperationCanceledException>());
            finishNotify.TrySetResult();
            Assert.That(!cancelled.IsConnected && notify.StopCount == stopCount + 1 && device.DisconnectCount == disconnectCount + 1, Is.True, "Cancelled StartNotify stops notifications and disconnects");
        }
        notify.OnStart = null;
        device.ResolveServices = false;
        var readinessOptions = new BlueZTransportOptions { BusAddress = boundAddress, ConnectionTimeout = TimeSpan.FromMilliseconds(200) };
        disconnectCount = device.DisconnectCount;
        await using (var unresolved = new BlueZBleTransport("B1:21:81:4B:E6:42", readinessOptions))
        {
            await Assert.ThatAsync(async () => await unresolved.ConnectAsync(), Throws.InstanceOf<TimeoutException>());
            Assert.That(!unresolved.IsConnected && device.DisconnectCount == disconnectCount + 1, Is.True, "Service-resolution timeout disconnects pending device");
        }
        device.ResolveServices = true;
        root.Objects.Remove(new(DevicePath));
        var timedOptions = new BlueZTransportOptions { BusAddress = boundAddress, DiscoveryTimeout = TimeSpan.FromMilliseconds(150), ConnectionTimeout = TimeSpan.FromSeconds(3) };
        var discoveryStops = adapter.DiscoveryStops;
        await using (var missing = new BlueZBleTransport("B1:21:81:4B:E6:42", timedOptions))
        {
            await Assert.ThatAsync(async () => await missing.ConnectAsync(), Throws.InstanceOf<TimeoutException>());
            Assert.That(!missing.IsConnected && adapter.DiscoveryStops == discoveryStops + 1, Is.True, "Discovery timeout releases own discovery session");
        }
        root.Objects[new(DevicePath)] = deviceObject;
    }

}
