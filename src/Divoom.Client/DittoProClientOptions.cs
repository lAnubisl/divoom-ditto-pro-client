namespace Divoom;

public sealed class DittoProClientOptions
{
    /// <summary>Supply a persisted UUID to keep controller identity stable across runs.</summary>
    public Guid ControllerId { get; init; } = Guid.NewGuid();
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Local;
    public TimeSpan AcknowledgementTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ChunkDelay { get; init; } = TimeSpan.FromMilliseconds(30);
    public TimeSpan CommandDelay { get; init; } = TimeSpan.FromMilliseconds(60);
    public TimeSpan InitializationPause { get; init; } = TimeSpan.FromSeconds(1);
    public Action<string>? Log { get; init; }
}
