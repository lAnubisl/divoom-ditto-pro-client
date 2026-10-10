namespace Divoom.Api;

public sealed record AudioPlaybackRequest(string? Url, double? Volume = null);
