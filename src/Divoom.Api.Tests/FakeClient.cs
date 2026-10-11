using Divoom;

sealed class FakeClient : IDittoProClient
{
    public int Images, Frames, CurrentTimes, Clocks, Disposals;
    public DateTimeOffset Time;
    public byte ClockStyle;
    public uint ClockColor;
    public Exception? Error;
    public Task SendImageAsync(DittoImage image, CancellationToken cancellationToken) { Images++; return Result(); }
    public Task SendAnimationAsync(IReadOnlyList<DittoAnimationFrame> frames, byte slot, CancellationToken cancellationToken) { Frames = frames.Count; return Result(); }
    public Task SendCurrentDateTimeAsync(CancellationToken cancellationToken) { CurrentTimes++; return Result(); }
    public Task ShowClockAsync(CancellationToken cancellationToken) { Clocks++; return Result(); }
    public Task ShowClockAsync(byte style, uint color, CancellationToken cancellationToken)
    { ClockStyle = style; ClockColor = color; Clocks++; return Result(); }
    public Task SendDateTimeAsync(DateTimeOffset value, CancellationToken cancellationToken) { Time = value; return Result(); }
    public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    private Task Result() => Error is null ? Task.CompletedTask : Task.FromException(Error);
}
