using System.Buffers.Binary;

namespace Divoom;

internal sealed class NotificationFrames
{
    private readonly List<byte> buffer = [];

    // Notifications can contain partial or multiple length-delimited SPP frames.
    public IReadOnlyList<byte[]> Append(byte[] bytes)
    {
        buffer.AddRange(bytes);
        var frames = new List<byte[]>();
        while (buffer.Count > 0)
        {
            var start = buffer.IndexOf(1);
            if (start < 0) { buffer.Clear(); break; }
            if (start > 0) buffer.RemoveRange(0, start);
            if (buffer.Count < 3) break;
            var length = buffer[1] | (buffer[2] << 8);
            if (length is < 3 or > 4096) { buffer.RemoveAt(0); continue; }
            var size = length + 4;
            if (buffer.Count < size) break;
            var frame = buffer.Take(size).ToArray();
            var checksum = frame.AsSpan(1, length).ToArray().Sum(b => (int)b) & 0xFFFF;
            if (frame[^1] != 2 || BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(size - 3)) != checksum)
            {
                buffer.RemoveAt(0);
                continue;
            }
            frames.Add(frame);
            buffer.RemoveRange(0, size);
        }
        return frames;
    }
}
