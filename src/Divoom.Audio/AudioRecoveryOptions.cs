namespace Divoom;

/// <summary>Recovery applies to the current station until stopped, replaced or disposed.</summary>
public sealed record AudioRecoveryOptions
{
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];
}
