using System.Buffers.Binary;
using Divoom;
using NUnit.Framework;

internal sealed class FakeTransport : IDittoProTransport
{
    private readonly List<byte> buffer = [];
    public bool IsConnected { get; private set; }
    public int MaxWriteSize { get; init; } = 138;
    public int ConnectCount { get; private set; }
    public int DisconnectCount { get; private set; }
    public bool Disposed { get; private set; }
    public bool SuppressImageAcknowledgement { get; set; }
    public bool SuppressClockAcknowledgement { get; set; }
    public int ClockFailuresRemaining { get; set; }
    public int ConnectFailuresRemaining { get; set; }
    public int UploadFailuresRemaining { get; set; }
    public bool RejectTime { get; init; }
    public bool RejectAnimation { get; init; }
    public bool SuppressAnimationReady { get; init; }
    public Action? ImageWritten { get; init; }
    public List<byte[]> Packets { get; } = [];
    public List<byte[]> Writes { get; } = [];
    public event Action<ReadOnlyMemory<byte>>? NotificationReceived;
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ConnectCount++;
        if (ConnectFailuresRemaining > 0) { ConnectFailuresRemaining--; throw new IOException("Transient connection failure"); }
        IsConnected = true; buffer.Clear(); return Task.CompletedTask;
    }
    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Assert.That(IsConnected && data.Length <= MaxWriteSize, Is.True, "Write contract");
        Writes.Add(data.ToArray()); buffer.AddRange(data.ToArray());
        if (buffer.Count < 6) return Task.CompletedTask;
        var size = 6 + buffer[4] + (buffer[5] << 8);
        if (buffer.Count < size) return Task.CompletedTask;
        Assert.That(buffer.Count == size, Is.True, "No command interleaving");
        var packet = buffer.ToArray(); buffer.Clear(); Packets.Add(packet);
        if (packet[11] == 0x45 && packet[12] == 0 && ClockFailuresRemaining > 0)
        { ClockFailuresRemaining--; throw new IOException("Transient clock write failure"); }
        if (packet[6] == 0 && packet[7] == 0x8B && UploadFailuresRemaining > 0)
        { UploadFailuresRemaining--; throw new IOException("Transient upload failure"); }
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(packet.Length - 2)) ==
            (packet.AsSpan(4, packet.Length - 6).ToArray().Sum(b => (int)b) & 65535), Is.True, "Independent packet checksum");
        if (packet[6] == 0 && packet[7] == 0x8B || packet[11] == 0x7B || packet[11] == 0x44 && SuppressImageAcknowledgement) return Task.CompletedTask;
        if (packet[11] == 0x45 && packet[12] == 0 && SuppressClockAcknowledgement) return Task.CompletedTask;
        if (packet[11] == 0x44) ImageWritten?.Invoke();
        var ack = Response([4, 0x33, 0x55, packet[7], 0, 0, 0]);
        NotificationReceived?.Invoke(ack.AsMemory(0, 4)); NotificationReceived?.Invoke(ack.AsMemory(4));
        if (packet[11] == 0x18) NotificationReceived?.Invoke(Response([4, 0x18, RejectTime ? (byte)0 : (byte)0x55]));
        if (packet[11] == 0x8B)
        {
            NotificationReceived?.Invoke(Response([4, 0x8B, 0x55, 1, 0, 0])); // Resend request is not start permission.
            if (!SuppressAnimationReady) NotificationReceived?.Invoke(Response([4, 0x8B, 0x55, 0, RejectAnimation ? (byte)0 : (byte)1]));
        }
        return Task.CompletedTask;
    }
    private static byte[] Response(byte[] payload)
    {
        var result = new byte[payload.Length + 6]; result[0] = 1; result[^1] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(1), (ushort)(payload.Length + 2)); payload.CopyTo(result, 3);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(result.Length - 3), (ushort)result.AsSpan(1, payload.Length + 2).ToArray().Sum(b => (int)b));
        return result;
    }
    public Task DisconnectAsync(CancellationToken cancellationToken = default) { DisconnectCount++; IsConnected = false; buffer.Clear(); return Task.CompletedTask; }
    public async ValueTask DisposeAsync() { Disposed = true; await DisconnectAsync(); }
}
