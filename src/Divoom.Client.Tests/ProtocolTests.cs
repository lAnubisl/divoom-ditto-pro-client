using System.Buffers.Binary;
using Divoom;
using NUnit.Framework;

public sealed class ProtocolTests
{
    [Test]
    public void TimePayloadMatchesGoldenVector()
    {
        var value = new DateTimeOffset(2026, 10, 4, 22, 30, 0, TimeSpan.FromHours(2));
        Assert.That(Convert.ToHexString(DivoomProtocol.TimePayload(value)), Is.EqualTo("181A140A04161E0000"));
    }

    [Test]
    public void TimePayloadHandlesCenturyBoundary()
    {
        Assert.That(DivoomProtocol.TimePayload(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero))[2], Is.EqualTo(21));
    }

    [Test]
    public void TimePayloadHandlesLeapDay()
    {
        Assert.That(DivoomProtocol.TimePayload(new DateTimeOffset(2024, 2, 29, 1, 2, 3, TimeSpan.Zero))[4], Is.EqualTo(29));
    }

    [Test]
    public void EnvelopeMatchesGoldenVector()
    {
        // Independent wire calculation: length=8, seq=0101, opcode=46, sum=0050.
        Assert.That(Convert.ToHexString(DivoomProtocol.Envelope([0x46], 0x0101)), Is.EqualTo("FEEFAA5508000101000000465000"));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(5)]
    [TestCase(16)]
    [TestCase(128)]
    public void ImagePalettePackingRoundTripsAcrossByteBoundaries(int count)
    {
        var rgb = new byte[768];
        for (var i = 0; i < 256; i++)
        {
            rgb[i * 3] = (byte)(i % count);
            rgb[i * 3 + 1] = (byte)(255 - i % count);
        }
        var payload = DivoomProtocol.EncodeImage(rgb);
        var bits = Math.Max(1, (int)Math.Ceiling(Math.Log2(count)));
        Assert.That(payload[11], Is.EqualTo(count));
        Assert.That(payload.Length, Is.EqualTo(12 + count * 3 + 32 * bits));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(6)), Is.EqualTo(payload.Length - 5));
        Assert.That(DivoomProtocol.PreviewPixels(payload), Is.EqualTo(rgb));
    }

    [Test]
    public void AlternatingPixelsArePackedLeastSignificantBitFirst()
    {
        var rgb = new byte[768];
        for (var i = 0; i < 256; i++) rgb[i * 3] = (byte)((i % 2) * 255);
        Assert.That(DivoomProtocol.EncodeImage(rgb)[18..], Is.All.EqualTo(0xAA));
    }

    [Test]
    public void ImageQuantizes256ColorsToEncodablePalette()
    {
        var rgb = new byte[768];
        for (var i = 0; i < 256; i++) { rgb[i * 3] = (byte)i; rgb[i * 3 + 1] = (byte)(255 - i); }
        Assert.That(DivoomProtocol.EncodeImage(rgb)[11], Is.LessThanOrEqualTo(128));
    }

    [Test]
    public void ImageRejectsInvalidPixelCount()
    {
        Assert.That(() => DivoomProtocol.EncodeImage(new byte[3]), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void NotificationDecoderReassemblesFragments()
    {
        var ack = Convert.FromHexString("01090004335526000000BB0002");
        var decoder = new NotificationFrames();
        Assert.That(decoder.Append(ack[..4]), Is.Empty);
        Assert.That(decoder.Append(ack[4..]).Single(), Is.EqualTo(ack));
    }

    [Test]
    public void NotificationDecoderHandlesMultipleFrames()
    {
        var ack = Convert.FromHexString("01090004335526000000BB0002");
        Assert.That(new NotificationFrames().Append([.. ack, .. ack]), Has.Count.EqualTo(2));
    }

    [Test]
    public void NotificationDecoderRejectsBadChecksumAndResynchronizes()
    {
        var ack = Convert.FromHexString("01090004335526000000BB0002");
        var bad = (byte[])ack.Clone();
        bad[^3] ^= 1;
        Assert.That(new NotificationFrames().Append([0xFF, .. bad, .. ack]).Single(), Is.EqualTo(ack));
    }
}
