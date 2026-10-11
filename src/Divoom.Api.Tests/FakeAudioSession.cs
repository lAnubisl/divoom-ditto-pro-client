using Divoom;

internal sealed class FakeAudioSession(bool autoPlay) : IAudioPlaybackSession
{
    public event Action<string>? StateChanged;
    public string State { get; private set; } = "starting";
    private double volume;
    public Action? OnVolumeSet { get; set; }
    public double Volume { get => volume; set { OnVolumeSet?.Invoke(); volume = value; } }
    public int Disposals { get; private set; }
    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Action<string>? SnapshotHandlers() => StateChanged;
    public void Play() { if (autoPlay) Emit("playing"); }
    public void Emit(string state) { State = state; StateChanged?.Invoke(state); }
    public void Dispose() { Disposals++; Disposed.TrySetResult(); }
}
