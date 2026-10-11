namespace Divoom;

internal sealed class AudioPlaybackRun(IAudioPlaybackSession session, Uri source)
{
    public IAudioPlaybackSession Session { get; } = session;
    public Uri Source { get; } = source;
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Action<string>? Handler { get; set; }
}
