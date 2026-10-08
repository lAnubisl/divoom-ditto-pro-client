using System.Buffers.Binary;

namespace Divoom;

// BLE framing and init observations: orionparrott/divoom-protocol (MIT).
// See THIRD-PARTY-NOTICES.txt. This encoder targets the modern 16x16 BLE family.
internal static class DivoomProtocol
{
    // A uint16 block index addresses 65,536 blocks of 256 bytes.
    // This is the wire representation boundary, not a measured firmware limit.
    private const int MaxAnimationBytes = (ushort.MaxValue + 1) * 256;

    // Ditoo Pro uses decimal year remainder + century, not ISO week or uint16 year.
    public static byte[] TimePayload(DateTimeOffset now) =>
        [0x18, (byte)(now.Year % 100), (byte)(now.Year / 100), (byte)now.Month, (byte)now.Day,
            (byte)now.Hour, (byte)now.Minute, (byte)now.Second, (byte)now.DayOfWeek];

    public static byte[] Envelope(ReadOnlySpan<byte> payload, ushort sequence)
    {
        var length = checked((ushort)(payload.Length + 7));
        var packet = new byte[length + 6];
        Convert.FromHexString("FEEFAA55").CopyTo(packet, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), length);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), sequence);
        payload.CopyTo(packet.AsSpan(11));
        var sum = 0;
        foreach (var b in packet.AsSpan(4, packet.Length - 6)) sum += b;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(packet.Length - 2), (ushort)sum);
        return packet;
    }

    public static byte[] EncodeImage(byte[] rgb)
    {
        if (rgb.Length != 768) throw new ArgumentException("Expected 256 RGB pixels.");
        var colors = Enumerable.Range(0, 256).Select(i => Color(rgb, i)).ToArray();
        // Keep original colors for pixel art; reduce complex images to at most 128.
        if (colors.Distinct().Count() > 128)
        {
            for (var i = 0; i < colors.Length; i++)
            {
                var r = ((colors[i] >> 16) & 255) >> 6;
                var g = ((colors[i] >> 8) & 255) >> 5;
                var b = (colors[i] & 255) >> 6;
                colors[i] = ((r * 255 / 3) << 16) | ((g * 255 / 7) << 8) | (b * 255 / 3);
            }
        }
        var palette = colors.Distinct().ToArray();
        var bits = Math.Max(1, (int)Math.Ceiling(Math.Log2(palette.Length)));
        var packed = new byte[256 * bits / 8];
        for (var i = 0; i < 256; i++)
        {
            var index = Array.IndexOf(palette, colors[i]);
            for (var bit = 0; bit < bits; bit++)
                if ((index & (1 << bit)) != 0) packed[(i * bits + bit) / 8] |= (byte)(1 << ((i * bits + bit) % 8));
        }
        var payload = new byte[12 + palette.Length * 3 + packed.Length];
        Convert.FromHexString("44000A0A04AA").CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)(7 + palette.Length * 3 + packed.Length));
        payload[8] = 0xF4; payload[9] = 1; payload[11] = (byte)palette.Length;
        for (var i = 0; i < palette.Length; i++)
        {
            payload[12 + i * 3] = (byte)(palette[i] >> 16);
            payload[13 + i * 3] = (byte)(palette[i] >> 8);
            payload[14 + i * 3] = (byte)palette[i];
        }
        packed.CopyTo(payload, 12 + palette.Length * 3);
        return payload;
    }

    private static int Color(byte[] rgb, int index) => (rgb[index * 3] << 16) | (rgb[index * 3 + 1] << 8) | rgb[index * 3 + 2];

    public static byte[] PreviewPixels(byte[] payload)
    {
        var count = payload[11];
        var bits = Math.Max(1, (int)Math.Ceiling(Math.Log2(count)));
        var pixels = new byte[768];
        var offset = 12 + count * 3;
        for (var i = 0; i < 256; i++)
        {
            var index = 0;
            for (var bit = 0; bit < bits; bit++)
                index |= ((payload[offset + (i * bits + bit) / 8] >> ((i * bits + bit) % 8)) & 1) << bit;
            payload.AsSpan(12 + index * 3, 3).CopyTo(pixels.AsSpan(i * 3, 3));
        }
        return pixels;
    }

    public static byte[] AnimationStream(IReadOnlyList<DittoAnimationFrame> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("Animation must contain at least one frame.", nameof(frames));
        using var stream = new MemoryStream();
        foreach (var frame in frames)
        {
            ArgumentNullException.ThrowIfNull(frame);
            var encoded = EncodeImage(frame.Image.Rgb.ToArray());
            var inner = encoded[5..];
            BinaryPrimitives.WriteUInt16LittleEndian(inner.AsSpan(3), (ushort)frame.Duration.TotalMilliseconds);
            inner[5] = 0; // Each frame supplies a complete palette; do not reuse the previous one.
            stream.Write(inner);
            if (stream.Length > MaxAnimationBytes) throw new ArgumentException("Animation exceeds the uint16 upload block index range.", nameof(frames));
        }
        return stream.ToArray();
    }

    public static byte[] AnimationAnnounce(int length)
    {
        if (length < 1 || length > MaxAnimationBytes) throw new ArgumentOutOfRangeException(nameof(length));
        var payload = new byte[6];
        payload[0] = 0x8B;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2), (uint)length);
        return payload;
    }

    public static byte[] AnimationChunk(int length, ushort index, ReadOnlySpan<byte> payload)
    {
        if (length < 1 || length > MaxAnimationBytes || index * 256 >= length ||
            payload.Length != Math.Min(256, length - index * 256)) throw new ArgumentOutOfRangeException(nameof(index));
        var body = new byte[7 + payload.Length];
        body[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(5), index);
        payload.CopyTo(body.AsSpan(7));
        return UploadPacket(body);
    }

    private static byte[] UploadPacket(ReadOnlySpan<byte> body)
    {
        var packet = new byte[body.Length + 10];
        Convert.FromHexString("FEEFAA55").CopyTo(packet, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), checked((ushort)(body.Length + 4)));
        packet[7] = 0x8B;
        body.CopyTo(packet.AsSpan(8));
        var sum = 0;
        foreach (var value in packet.AsSpan(4, packet.Length - 6)) sum += value;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(packet.Length - 2), (ushort)sum);
        return packet;
    }
}
