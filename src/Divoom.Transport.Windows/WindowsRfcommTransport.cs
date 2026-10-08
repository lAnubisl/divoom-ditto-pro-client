using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Divoom;

/// <summary>Ordered Windows RFCOMM byte channel. Ditoo Pro uses BLE for display commands; this channel supports Classic connection diagnostics.</summary>
public sealed class WindowsRfcommTransport : IDittoProTransport
{
    private readonly ulong address;
    private BluetoothDevice? device;
    private StreamSocket? socket;
    private DataReader? reader;
    private DataWriter? writer;
    private CancellationTokenSource? receiveCancellation;
    private Task? receiveTask;
    private bool disposed;
    private volatile bool connected;

    public WindowsRfcommTransport(ulong bluetoothAddress)
    {
        if (bluetoothAddress is 0 or > 0xFFFFFFFFFFFF) throw new ArgumentOutOfRangeException(nameof(bluetoothAddress));
        address = bluetoothAddress;
    }
    public bool IsConnected => connected;
    public int MaxWriteSize => connected ? 138 : 0;
    public event Action<ReadOnlyMemory<byte>>? NotificationReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connected) return;
        await DisconnectAsync(cancellationToken);
        try
        {
            device = await BluetoothDevice.FromBluetoothAddressAsync(address).AsTask(cancellationToken)
                ?? throw new IOException("Windows denied access to the Classic endpoint.");
            var result = await device.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            try
            {
                if (result.Error != BluetoothError.Success || result.Services.Count == 0) throw new IOException($"SPP discovery failed: {result.Error}; services={result.Services.Count}.");
                Exception? lastError = null;
                foreach (var service in result.Services)
                {
                    socket = new StreamSocket();
                    try
                    {
                        await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, service.ProtectionLevel).AsTask(cancellationToken);
                        lastError = null;
                        break;
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        socket.Dispose(); socket = null; lastError = error;
                    }
                }
                if (socket is null) throw new IOException("No RFCOMM service accepted a connection.", lastError);
            }
            finally { foreach (var service in result.Services) service.Dispose(); }
            reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            writer = new DataWriter(socket.OutputStream);
            receiveCancellation = new CancellationTokenSource();
            connected = true;
            receiveTask = ReadAsync(receiveCancellation.Token);
        }
        catch
        {
            await DisconnectAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ReadAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var size = await reader!.LoadAsync(138).AsTask(token);
                if (size == 0) break;
                var bytes = new byte[size]; reader.ReadBytes(bytes);
                NotificationReceived?.Invoke(bytes);
            }
        }
        catch (Exception) { /* EOF, remote disconnect or cancelled read closes the channel. */ }
        finally { connected = false; }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!connected || writer is null) throw new IOException("RFCOMM disconnected.");
        if (data.IsEmpty || data.Length > MaxWriteSize) throw new ArgumentOutOfRangeException(nameof(data));
        writer.WriteBytes(data.ToArray());
        if (await writer.StoreAsync().AsTask(cancellationToken) != data.Length) throw new IOException("Incomplete RFCOMM write.");
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        connected = false;
        receiveCancellation?.Cancel();
        socket?.Dispose();
        try { if (receiveTask is not null) await receiveTask.WaitAsync(cancellationToken); }
        finally
        {
            reader?.Dispose(); writer?.Dispose(); device?.Dispose(); receiveCancellation?.Dispose();
            socket = null; reader = null; writer = null; device = null; receiveCancellation = null; receiveTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await DisconnectAsync();
    }
}
