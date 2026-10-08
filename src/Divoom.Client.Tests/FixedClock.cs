internal sealed class FixedClock : TimeProvider
{
    public int Calls { get; private set; }
    public override DateTimeOffset GetUtcNow() { Calls++; return new(2026, 10, 4, 20, 30, 0, TimeSpan.Zero); }
}
