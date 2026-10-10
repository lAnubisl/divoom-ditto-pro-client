namespace Divoom;

/// <summary>Device operations with lazy connection and session reuse until disposal. Owns and asynchronously disposes the underlying transport.</summary>
public interface IDittoProClient : IAsyncDisposable
{
    /// <summary>Select clock style zero in green. Does not change the time or startup channel.</summary>
    Task ShowClockAsync(CancellationToken cancellationToken = default);

    /// <summary>Select a built-in clock style (0..15) and RGB color (0x000000..0xFFFFFF). Does not change the time or startup channel.</summary>
    Task ShowClockAsync(byte style, uint color, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This client does not support clock appearance settings.");

    /// <summary>Send a 16x16 image using the shared client connection.</summary>
    Task SendImageAsync(DittoImage image, CancellationToken cancellationToken = default);

    /// <summary>Upload a looping animation. Slot is reserved and must be zero. Persistence across power-off is not guaranteed.</summary>
    Task SendAnimationAsync(IReadOnlyList<DittoAnimationFrame> frames, byte slot = 0, CancellationToken cancellationToken = default);

    /// <summary>Sample the clock after connecting and send the current date/time in the configured time zone.</summary>
    Task SendCurrentDateTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>Send a specific wall-clock date/time using the supplied offset.</summary>
    Task SendDateTimeAsync(DateTimeOffset value, CancellationToken cancellationToken = default);
}
