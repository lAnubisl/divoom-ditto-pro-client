namespace Divoom;

public interface IAudioPlaybackSession : IDisposable
{
    /// <summary>Reports starting, buffering, playing, stopped, or failed from any thread.</summary>
    event Action<string>? StateChanged;
    string State { get; }
    double Volume { get; set; }
    void Play();
}
