namespace Divoom;

/// <summary>Device operations with lazy connection and session reuse until disposal. Owns and asynchronously disposes the underlying transport.</summary>
public interface IDittoProClient : IAsyncDisposable
{
    /// <summary>Select the built-in clock display. Does not change the time or startup channel.</summary>
    Task ShowClockAsync(CancellationToken cancellationToken = default);

    /// <summary>Send a 16x16 image using the shared client connection.</summary>
    Task SendImageAsync(DittoImage image, CancellationToken cancellationToken = default);

    /// <summary>Upload a looping animation. Slot is reserved and must be zero. Persistence across power-off is not guaranteed.</summary>
    Task SendAnimationAsync(IReadOnlyList<DittoAnimationFrame> frames, byte slot = 0, CancellationToken cancellationToken = default);

    /// <summary>Sample the clock after connecting and send the current date/time in the configured time zone.</summary>
    Task SendCurrentDateTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>Send a specific wall-clock date/time using the supplied offset.</summary>
    Task SendDateTimeAsync(DateTimeOffset value, CancellationToken cancellationToken = default);
}
