namespace Divoom;

/// <summary>Player state; errors are stable codes rather than platform exception details.</summary>
public sealed record AudioPlaybackStatus(string State, double Volume, string? Error,
    int RetryAttempts, string? LastError);
