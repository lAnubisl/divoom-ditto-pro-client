using Divoom;

internal sealed class FakeAudioBackend : IAudioPlaybackBackend
{
    public List<FakeAudioSession> Sessions { get; } = [];
    public Uri? Source { get; private set; }
    public bool AutoPlay { get; set; } = true;
    public Exception? Error { get; set; }
    public Action? OnOpen { get; set; }

    public Task<IAudioPlaybackSession> OpenAsync(Uri source, double volume, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Error is not null) return Task.FromException<IAudioPlaybackSession>(Error);
        Source = source;
        var session = new FakeAudioSession(AutoPlay) { Volume = volume };
        Sessions.Add(session);
        OnOpen?.Invoke();
        return Task.FromResult<IAudioPlaybackSession>(session);
    }
}
