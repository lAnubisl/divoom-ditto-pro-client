namespace Divoom;

/// <summary>A reliable, ordered byte transport. Display commands use BLE UART through Windows GATT or Linux BlueZ. DisposeAsync must disconnect and release all resources and must be idempotent.</summary>
public interface IDittoProTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    /// <summary>Maximum write size in bytes, valid after ConnectAsync. For BLE this is ATT MTU minus three.</summary>
    int MaxWriteSize { get; }
    /// <summary>May arrive on any thread; implementations must provide stable, copied bytes.</summary>
    event Action<ReadOnlyMemory<byte>>? NotificationReceived;
    /// <summary>Connect and subscribe to notifications before returning. Must support reconnect after DisconnectAsync.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);
    /// <summary>Write one chunk with flow control. Throw when transmission fails.</summary>
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
