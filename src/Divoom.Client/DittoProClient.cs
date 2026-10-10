using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Divoom;

/// <summary>Platform-independent Ditoo Pro client. Connects lazily, reuses the session and disconnects on disposal; owns its injected transport.</summary>
public sealed class DittoProClient : IDittoProClient
{
    private readonly IDittoProTransport transport;
    private readonly DittoProClientOptions options;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object receiveLock = new();
    private NotificationFrames decoder = new();
    private TaskCompletionSource<bool>? acknowledgement;
    private int expectedSequence = -1;
    private int expectedCommand = -1;
    private ushort sequence;
    private byte imageSlot;
    private bool initialized;
    private bool disposed;

    public DittoProClient(IDittoProTransport transport, DittoProClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        this.transport = transport;
        this.options = options ?? new();
        ArgumentNullException.ThrowIfNull(this.options.TimeProvider);
        ArgumentNullException.ThrowIfNull(this.options.TimeZone);
        if (this.options.ControllerId == Guid.Empty || this.options.AcknowledgementTimeout <= TimeSpan.Zero ||
            this.options.ChunkDelay < TimeSpan.Zero || this.options.CommandDelay < TimeSpan.Zero || this.options.InitializationPause < TimeSpan.Zero)
            throw new ArgumentException("Invalid client options.", nameof(options));
        transport.NotificationReceived += Receive;
    }

    /// <summary>Reserved for internal self-diagnostics: check the byte channel without sending device commands and keep it open.</summary>
    private async Task CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operations.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            try
            {
                if (!transport.IsConnected)
                {
                    initialized = false;
                    await transport.ConnectAsync(cancellationToken);
                }
                if (!transport.IsConnected || transport.MaxWriteSize < 1) throw new IOException("Transport did not establish a writable connection.");
                options.Log?.Invoke("CONNECTED: transport connection verified; no device commands sent.");
            }
            catch
            {
                await DisconnectTransportAsync(suppressErrors: true);
                throw;
            }
        }
        finally { operations.Release(); }
    }

    /// <inheritdoc />
    public Task SendImageAsync(DittoImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var payload = DivoomProtocol.EncodeImage(image.Rgb.ToArray());
        return RunAsync(token => SendImagePayloadAsync(payload, token), cancellationToken);
    }

    private async Task SendImagePayloadAsync(byte[] payload, CancellationToken token)
    {
        if (payload[11] == 1)
            await SendPayloadAsync([0x45, 1, payload[12], payload[13], payload[14], 100, 0, 1], token);
        else
        {
            var slot = imageSlot;
            imageSlot = unchecked((byte)(imageSlot + 3));
            await SendPayloadAsync([0xBD, 0x31, slot, 1], token);
            await SendPayloadAsync([0x9F, unchecked((byte)(slot + 0xB1))], token);
            await SendPayloadAsync(payload, token);
        }
        options.Log?.Invoke("IMAGE_RECEIVED: image command acknowledged by device.");
    }

    /// <inheritdoc />
    public Task SendAnimationAsync(IReadOnlyList<DittoAnimationFrame> frames, byte slot = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (slot != 0) throw new NotSupportedException("The Ditoo Pro animation transfer has no slot field; use slot zero.");
        var stream = DivoomProtocol.AnimationStream(frames.ToArray());
        var count = (stream.Length + 255) / 256;
        return RunAsync(async token =>
        {
            await SendPayloadAsync(DivoomProtocol.AnimationAnnounce(stream.Length), token, commandAcknowledgement: 0x8B);
            for (var index = 0; index < count; index++)
            {
                var offset = index * 256;
                var packet = DivoomProtocol.AnimationChunk(stream.Length, checked((ushort)index),
                    stream.AsSpan(offset, Math.Min(256, stream.Length - offset)));
                await WritePacketAsync(packet, token);
                await Task.Delay(options.CommandDelay, token);
            }
            options.Log?.Invoke($"ANIMATION_SENT: {frames.Count} frames; {count} upload chunks. Display rendering and persistent storage are not confirmed by transport writes.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task SendCurrentDateTimeAsync(CancellationToken cancellationToken = default) =>
        RunAsync(token => SendTimeAsync(CurrentTime(), token), cancellationToken);

    /// <inheritdoc />
    public Task SendDateTimeAsync(DateTimeOffset value, CancellationToken cancellationToken = default) =>
        RunAsync(token => SendTimeAsync(value, token), cancellationToken);

    /// <inheritdoc />
    public Task ShowClockAsync(CancellationToken cancellationToken = default) =>
        ShowClockAsync(0, 0x00FF00, cancellationToken);

    /// <inheritdoc />
    public Task ShowClockAsync(byte style, uint color, CancellationToken cancellationToken = default)
    {
        if (style > 15) throw new ArgumentOutOfRangeException(nameof(style));
        if (color > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(color));
        return RunAsync(async token =>
        {
            await SendPayloadAsync([0x45, 0x00, 0x01, style, 0x01, 0x00, 0x00, 0x00,
                (byte)(color >> 16), (byte)(color >> 8), (byte)color], token);
            options.Log?.Invoke($"CLOCK_SELECTED: style={style}; color=#{color:X6}; command acknowledged; display rendering is not verified.");
        }, cancellationToken);
    }

    private DateTimeOffset CurrentTime() => TimeZoneInfo.ConvertTime(options.TimeProvider.GetUtcNow(), options.TimeZone);

    private async Task SendTimeAsync(DateTimeOffset now, CancellationToken token)
    {
        await SendPayloadAsync(DivoomProtocol.TimePayload(now), token, commandAcknowledgement: 0x18);
        options.Log?.Invoke($"TIME_SENT: {now:O}; command acknowledged; device clock readback is not verified.");
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operations.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    await InitializeAsync(token);
                    await action(token);
                    return;
                }
                catch (Exception error)
                {
                    // Replay the whole operation on a new session, never a fragment of an upload.
                    await DisconnectTransportAsync(suppressErrors: true);
                    if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                    if (attempt >= 3 || !IsRetryable(error)) throw;
                    options.Log?.Invoke($"RETRY: attempt {attempt + 1}/3 in 1 second after {error.GetType().Name}.");
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
            }
        }
        finally { operations.Release(); }
    }

    private static bool IsRetryable(Exception error) =>
        error is IOException or TimeoutException or System.Runtime.InteropServices.COMException ||
        // Keep the portable client independent of the Linux transport's D-Bus package.
        error.GetType().FullName is "Tmds.DBus.DBusException" or "Tmds.DBus.DisconnectedException";

    private async Task DisconnectTransportAsync(bool suppressErrors = false)
    {
        initialized = false;
        lock (receiveLock) { acknowledgement = null; expectedSequence = -1; expectedCommand = -1; decoder = new(); }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await transport.DisconnectAsync(cleanup.Token); }
        catch when (suppressErrors) { /* Preserve the original connection, transfer or cancellation failure. */ }
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        if (initialized && transport.IsConnected) return;
        initialized = false;
        if (transport.IsConnected) await transport.DisconnectAsync(token);
        lock (receiveLock) { decoder = new(); acknowledgement = null; }
        await transport.ConnectAsync(token);
        if (!transport.IsConnected || transport.MaxWriteSize < 1) throw new IOException("Transport did not establish a writable BLE connection.");
        sequence = 1;
        imageSlot = 0;
        var now = CurrentTime();
        var json = JsonSerializer.Serialize(new { Command = "Device/SetUTC", Utc = now.ToUnixTimeSeconds(), Time = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) }).Replace("/", "\\/");
        await SendPayloadAsync(Encoding.UTF8.GetBytes(json), token, waitForAcknowledgement: false);
        sequence = 0x0101;
        await SendTimeAsync(now, token);
        string[] init = ["BD1F01", "8E00", "BD13", "BD18", "BD15", "BD17FF", "BD2500", "BD2600", "BD27", "46",
            "26FF", "15", "B6FF", "9700", "36", "3700", "8A00", "B0", "A2", "42", "59", "76"];
        foreach (var hex in init) await SendPayloadAsync(Convert.FromHexString(hex), token);
        var controller = new byte[] { 0x4F }.Concat(Encoding.ASCII.GetBytes(options.ControllerId.ToString().ToUpperInvariant()[..16])).ToArray();
        await SendPayloadAsync(controller, token);
        foreach (var hex in new[] { "76", "2702", "5F150B" }) await SendPayloadAsync(Convert.FromHexString(hex), token);
        // Opaque display configuration observed during normal iOS initialization; see third-party notices.
        await SendPayloadAsync(Convert.FromHexString("5DEA0705111400281602140B120A100A120113011501150111010E0A0D0A0D0A0F0114011601130111010E0A0D0A0C0C0D041104120310040D020C0B0B0C0B0C0D03110313020F020E010D0A0C0A0C0A0D02100311031003"), token);
        await SendPayloadAsync(Convert.FromHexString("9B014E6F620241040000"), token);
        await Task.Delay(options.InitializationPause, token);
        await SendPayloadAsync([0xBA, 1], token);
        await SendPayloadAsync(controller, token);
        foreach (var hex in new[] { "76", "2702", "BD2F02", "31" }) await SendPayloadAsync(Convert.FromHexString(hex), token);
        initialized = true;
        options.Log?.Invoke("SESSION_READY");
    }

    private async Task SendPayloadAsync(byte[] payload, CancellationToken token, bool waitForAcknowledgement = true, int commandAcknowledgement = -1)
    {
        var current = sequence++;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (receiveLock)
        {
            acknowledgement = waitForAcknowledgement ? pending : null;
            expectedSequence = current & 255;
            expectedCommand = commandAcknowledgement;
        }
        try
        {
            options.Log?.Invoke($"SEND: seq={current:X4}; opcode={payload[0]:X2}");
            await WritePacketAsync(DivoomProtocol.Envelope(payload, current), token);
            if (waitForAcknowledgement) await pending.Task.WaitAsync(options.AcknowledgementTimeout, token);
            await Task.Delay(options.CommandDelay, token);
        }
        finally { lock (receiveLock) { acknowledgement = null; expectedSequence = -1; expectedCommand = -1; } }
    }

    private async Task WritePacketAsync(byte[] packet, CancellationToken token)
    {
        if (!transport.IsConnected) throw new IOException("BLE transport disconnected.");
        var size = Math.Min(138, transport.MaxWriteSize);
        if (size < 1) throw new IOException("Invalid negotiated write size.");
        for (var offset = 0; offset < packet.Length; offset += size)
        {
            await transport.WriteAsync(packet.AsMemory(offset, Math.Min(size, packet.Length - offset)), token);
            await Task.Delay(options.ChunkDelay, token);
        }
    }

    private void Receive(ReadOnlyMemory<byte> bytes)
    {
        lock (receiveLock)
        {
            foreach (var frame in decoder.Append(bytes.ToArray()))
            {
                if (frame.Length < 9 || frame[3] != 4 || acknowledgement is null) continue;
                if (expectedCommand >= 0 && frame[4] == expectedCommand)
                {
                    if (expectedCommand == 0x8B && frame[5] == 0x55)
                    {
                        // 0x8b/0: explicit permission to start. A resend request is not an ACK.
                        if (frame.Length < 11 || frame[6] != 0) continue;
                        if (frame[7] != 1) acknowledgement.TrySetException(new IOException("Device refused animation transfer."));
                        else acknowledgement.TrySetResult(true);
                        continue;
                    }
                    if (frame[5] == 0x55) acknowledgement.TrySetResult(true);
                    else acknowledgement.TrySetException(new IOException($"Device rejected command {expectedCommand:X2}: status {frame[5]:X2}."));
                }
                else if (expectedCommand < 0 && frame.Length >= 13 && frame[4] == 0x33 && frame[5] == 0x55 && frame[6] == expectedSequence)
                    acknowledgement.TrySetResult(true);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            transport.NotificationReceived -= Receive;
            initialized = false;
            lock (receiveLock) { acknowledgement = null; expectedSequence = -1; expectedCommand = -1; decoder = new(); }
            await transport.DisposeAsync();
        }
        finally { operations.Release(); }
    }
}
