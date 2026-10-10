using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Divoom;

internal sealed class WindowsAudioPlaybackSession : IAudioPlaybackSession
{
    private readonly MediaPlayer player;
    private readonly MediaSource source;
    private string state = "starting";
    private int disposed;

    public WindowsAudioPlaybackSession(DeviceInformation output, Uri stream, double volume)
    {
        player = new MediaPlayer();
        MediaSource? createdSource = null;
        try
        {
            player.AudioDevice = output;
            player.Volume = volume;
            source = createdSource = MediaSource.CreateFromUri(stream);
            player.MediaFailed += OnFailed;
            player.MediaEnded += OnEnded;
            player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            player.Source = source;
        }
        catch
        {
            try { player.Dispose(); }
            finally { createdSource?.Dispose(); }
            throw;
        }
    }

    public event Action<string>? StateChanged;
    public string State => Volatile.Read(ref state);
    public double Volume { get => player.Volume; set => player.Volume = value; }
    public void Play() => player.Play();

    private void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => Update("failed");
    private void OnEnded(MediaPlayer sender, object args) => Update("stopped");
    private void OnPlaybackStateChanged(MediaPlaybackSession session, object args)
    {
        if (Volatile.Read(ref disposed) != 0 || State is "failed" or "stopped") return;
        try
        {
            switch (session.PlaybackState)
            {
                case MediaPlaybackState.Playing: Update("playing"); break;
                case MediaPlaybackState.Buffering: Update("buffering"); break;
                case MediaPlaybackState.Opening: Update("starting"); break;
            }
        }
        catch (Exception error) when (error is ObjectDisposedException or System.Runtime.InteropServices.COMException)
        { Update("failed"); }
    }

    private void Update(string value)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Volatile.Write(ref state, value);
        StateChanged?.Invoke(value);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        player.MediaFailed -= OnFailed;
        player.MediaEnded -= OnEnded;
        player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        try { player.Dispose(); }
        finally { source.Dispose(); }
    }
}
