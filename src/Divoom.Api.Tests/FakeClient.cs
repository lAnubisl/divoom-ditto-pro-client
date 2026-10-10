using Divoom;

sealed class FakeClient : IDittoProClient
{
    public int Images, Frames, CurrentTimes, Clocks, Disposals;
    public DateTimeOffset Time;
    public byte ClockStyle;
    public uint ClockColor;
    public Exception? Error;
    public Task SendImageAsync(DittoImage image, CancellationToken cancellationToken = default) { Images++; return Result(); }
    public Task SendAnimationAsync(IReadOnlyList<DittoAnimationFrame> frames, byte slot = 0, CancellationToken cancellationToken = default) { Frames = frames.Count; return Result(); }
    public Task SendCurrentDateTimeAsync(CancellationToken cancellationToken = default) { CurrentTimes++; return Result(); }
    public Task ShowClockAsync(CancellationToken cancellationToken = default) { Clocks++; return Result(); }
    public Task ShowClockAsync(byte style, uint color, CancellationToken cancellationToken = default)
    { ClockStyle = style; ClockColor = color; Clocks++; return Result(); }
    public Task SendDateTimeAsync(DateTimeOffset value, CancellationToken cancellationToken = default) { Time = value; return Result(); }
    public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    private Task Result() => Error is null ? Task.CompletedTask : Task.FromException(Error);
}
