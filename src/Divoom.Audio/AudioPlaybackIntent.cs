namespace Divoom;

internal sealed class AudioPlaybackIntent(Uri source)
{
    public Uri Source { get; } = source;
    public CancellationTokenSource Cancellation { get; } = new();
    public Task Worker { get; set; } = Task.CompletedTask;
}
