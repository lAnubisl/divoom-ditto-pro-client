using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Divoom;

internal sealed class LinuxAudioPlaybackSession : IAudioPlaybackSession
{
    private readonly Process process;
    private readonly Socket socket;
    private readonly NetworkStream connection;
    private readonly StreamWriter writer;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly Uri source;
    private readonly string device;
    private readonly string directory;
    private readonly Task reader;
    private readonly Task processExit;
    private readonly Task output;
    private readonly Task errors;
    private long sequence;
    private int disposed;
    private string state = "starting";
    private double volume;
    private bool audioReady;
    private bool restarted;

    private LinuxAudioPlaybackSession(Process process, Socket socket, string directory, string device, Uri source, double volume)
    {
        this.process = process;
        this.socket = socket;
        this.directory = directory;
        this.device = device;
        this.source = source;
        this.volume = volume;
        connection = new NetworkStream(socket, ownsSocket: false);
        writer = new StreamWriter(connection, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        reader = ReadEventsAsync();
        output = DrainAsync(process.StandardOutput, lifetime.Token);
        errors = DrainAsync(process.StandardError, lifetime.Token);
        processExit = WatchProcessAsync();
    }

    public event Action<string>? StateChanged;
    public string State => Volatile.Read(ref state);
    public double Volume
    {
        get => Volatile.Read(ref volume);
        set
        {
            SendAsync(["set_property", "volume", value * 100], CancellationToken.None).GetAwaiter().GetResult();
            Volatile.Write(ref volume, value);
        }
    }

    public static async Task<LinuxAudioPlaybackSession> OpenAsync(string sink, Uri source, double volume, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var directory = Path.Combine(Path.GetTempPath(), "divoom-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var socketPath = Path.Combine(directory, "ipc");
        var device = "pulse/" + sink;
        var start = LinuxAudioProcess.CreateStartInfo(device, volume, socketPath);
        Process? process = null;
        Socket? socket = null;
        LinuxAudioPlaybackSession? session = null;
        try
        {
            process = Process.Start(start) ?? throw new AudioUnavailableException("mpv did not start.");
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(TimeSpan.FromSeconds(5));
            while (socket is null)
            {
                startup.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new AudioUnavailableException("mpv exited before opening audio control.");
                if (File.Exists(socketPath))
                {
                    var candidate = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try { await candidate.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), startup.Token); socket = candidate; }
                    catch (SocketException) { candidate.Dispose(); }
                    catch { candidate.Dispose(); throw; }
                }
                if (socket is null) await Task.Delay(50, startup.Token);
            }
            session = new LinuxAudioPlaybackSession(process, socket, directory, device, source, volume);
            var devices = await session.SendAsync(["get_property", "audio-device-list"], cancellationToken);
            if (!HasDevice(devices, device)) throw new AudioUnavailableException("The configured PulseAudio sink is unavailable.");
            await session.SendAsync(["observe_property", 1, "audio-params"], cancellationToken);
            await session.SendAsync(["observe_property", 2, "paused-for-cache"], cancellationToken);
            await session.SendAsync(["observe_property", 3, "audio-device-list"], cancellationToken);
            return session;
        }
        catch
        {
            if (session is not null) session.Dispose();
            else
            {
                socket?.Dispose();
                if (process is not null) { Terminate(process); process.Dispose(); }
                RemoveDirectory(directory);
            }
            throw;
        }
    }

    public void Play() => SendAsync(["loadfile", source.AbsoluteUri, "replace"], CancellationToken.None).GetAwaiter().GetResult();

    private async Task<JsonElement> SendAsync(object[] command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var id = Interlocked.Increment(ref sequence);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = reply;
        try
        {
            await writes.WaitAsync(timeout.Token);
            try { await writer.WriteLineAsync(JsonSerializer.Serialize(new { command, request_id = id }).AsMemory(), timeout.Token); }
            finally { writes.Release(); }
            return await reply.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new AudioUnavailableException("Linux audio control timed out or disconnected."); }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task ReadEventsAsync()
    {
        var token = lifetime.Token;
        using var lines = new StreamReader(connection, Encoding.UTF8, leaveOpen: true);
        try
        {
            while (await lines.ReadLineAsync(token) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.TryGetProperty("request_id", out var id) && pending.TryGetValue(id.GetInt64(), out var reply))
                {
                    if (message.GetProperty("error").GetString() == "success")
                        reply.TrySetResult(message.TryGetProperty("data", out var data) ? data.Clone() : default);
                    else reply.TrySetException(new AudioUnavailableException("mpv rejected an audio control command."));
                }
                else HandleEvent(message);
            }
            Update("failed");
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException or JsonException)
        { Update("failed"); }
        finally
        {
            foreach (var reply in pending.Values) reply.TrySetException(new AudioUnavailableException("Linux audio control disconnected."));
        }
    }

    private void HandleEvent(JsonElement message)
    {
        if (!message.TryGetProperty("event", out var eventName)) return;
        switch (eventName.GetString())
        {
            case "playback-restart": restarted = true; if (audioReady) Update("playing"); break;
            case "end-file":
                var failed = message.TryGetProperty("reason", out var reason) && reason.GetString() == "error";
                Update(failed ? "failed" : "stopped");
                break;
            case "property-change" when message.TryGetProperty("data", out var data):
                switch (message.GetProperty("name").GetString())
                {
                    case "audio-params": audioReady = data.ValueKind == JsonValueKind.Object; if (audioReady && restarted) Update("playing"); break;
                    case "paused-for-cache" when data.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        if (data.GetBoolean()) Update("buffering"); else if (audioReady && restarted) Update("playing");
                        break;
                    case "audio-device-list": if (!HasDevice(data, device)) Update("failed"); break;
                }
                break;
        }
    }

    private static bool HasDevice(JsonElement data, string device) => data.ValueKind == JsonValueKind.Array &&
        data.EnumerateArray().Any(item => item.TryGetProperty("name", out var name) && name.GetString() == device);

    private void Update(string value)
    {
        if (Volatile.Read(ref disposed) != 0 || State is "failed" or "stopped") return;
        Volatile.Write(ref state, value);
        StateChanged?.Invoke(value);
    }

    private async Task WatchProcessAsync()
    {
        try { await process.WaitForExitAsync(lifetime.Token); Update("failed"); }
        catch (OperationCanceledException) { }
    }

    private static async Task DrainAsync(StreamReader stream, CancellationToken cancellationToken)
    {
        try { while (await stream.ReadLineAsync(cancellationToken) is not null) { } }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        socket.Dispose();
        Terminate(process);
        try { Task.WhenAll(reader, processExit, output, errors).Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        try { writer.Dispose(); }
        catch (IOException) { }
        connection.Dispose();
        process.Dispose();
        lifetime.Dispose();
        RemoveDirectory(directory);
    }

    private static void Terminate(Process process)
    {
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(2000); } }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static void RemoveDirectory(string directory)
    {
        try { File.Delete(Path.Combine(directory, "ipc")); Directory.Delete(directory); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
