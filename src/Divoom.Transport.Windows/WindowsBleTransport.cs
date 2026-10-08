using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Divoom;

public sealed class WindowsBleTransport : IDittoProTransport
{
    private readonly ulong address;
    private BluetoothLEDevice? device;
    private GattDeviceService? service;
    private GattSession? session;
    private GattCharacteristic? write;
    private GattCharacteristic? notify;
    private GattWriteOption writeMode;
    private bool subscribed;
    private bool disposed;

    public event Action<ReadOnlyMemory<byte>>? NotificationReceived;
    public bool IsConnected => device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
    public int MaxWriteSize => session is null ? 0 : session.MaxPduSize - 3;

    public WindowsBleTransport(ulong bluetoothAddress)
    {
        if (bluetoothAddress is 0 or > 0xFFFFFFFFFFFF) throw new ArgumentOutOfRangeException(nameof(bluetoothAddress));
        address = bluetoothAddress;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsConnected && subscribed) return;
        await DisconnectAsync(cancellationToken);
        try
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancellationToken)
                ?? throw new IOException("Windows denied BLE access.");
            var services = await device.GetGattServicesForUuidAsync(DittoProUart.Service, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count != 1)
            {
                foreach (var item in services.Services) item.Dispose();
                throw new IOException($"Divoom UART service discovery failed: {services.Status}.");
            }
            service = services.Services[0];
            var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            Ensure(chars.Status, "Characteristic discovery");
            write = chars.Characteristics.SingleOrDefault(c => c.Uuid == DittoProUart.Write)
                ?? throw new IOException("Divoom UART write characteristic missing.");
            notify = chars.Characteristics.SingleOrDefault(c => c.Uuid == DittoProUart.Notify)
                ?? throw new IOException("Divoom UART notification characteristic missing.");
            writeMode = write.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
            if (!write.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) &&
                !write.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                throw new IOException("UART characteristic is not writable.");
            var config = notify.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(cancellationToken)
                ?? throw new IOException("Could not open GATT session.");
            session.MaintainConnection = true;
            notify.ValueChanged += Receive;
            Ensure(await notify.WriteClientCharacteristicConfigurationDescriptorAsync(config).AsTask(cancellationToken), "Subscribe");
            subscribed = true;
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await DisconnectAsync(cleanup.Token); } catch { }
            throw;
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (write is null || !IsConnected) throw new IOException("BLE is disconnected.");
        if (data.IsEmpty || data.Length > MaxWriteSize) throw new ArgumentOutOfRangeException(nameof(data));
        var result = await write.WriteValueWithResultAsync(data.ToArray().AsBuffer(), writeMode).AsTask(cancellationToken);
        Ensure(result.Status, $"GATT write (ATT error {result.ProtocolError})");
    }

    private void Receive(GattCharacteristic sender, GattValueChangedEventArgs args) =>
        NotificationReceived?.Invoke(args.CharacteristicValue.ToArray());

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (notify is not null)
            {
                notify.ValueChanged -= Receive;
                if (subscribed && IsConnected)
                    await notify.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None).AsTask(cancellationToken);
            }
        }
        finally
        {
            subscribed = false;
            if (session is not null) { session.MaintainConnection = false; session.Dispose(); }
            service?.Dispose();
            device?.Dispose();
            session = null; service = null; device = null; write = null; notify = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await DisconnectAsync(cleanup.Token);
    }

    private static void Ensure(GattCommunicationStatus status, string operation)
    {
        if (status != GattCommunicationStatus.Success) throw new IOException($"{operation}: {status}");
    }
}
