using System.Buffers.Binary;
using System.Text.Json;
using Divoom;
using NUnit.Framework;

public sealed class ClientTests
{
    private DittoImage image = null!;
    private DittoAnimationFrame[] frames = null!;
    private byte[] inner = null!;

    [SetUp]
    public void SetUp()
    {
        var rgb = new byte[768];
        for (var i = 0; i < 256; i++) { rgb[i * 3] = (byte)(i % 16); rgb[i * 3 + 1] = 128; }
        image = new DittoImage(rgb); rgb[0] = 100;
        frames = new[] { new DittoAnimationFrame(image, TimeSpan.FromMilliseconds(150)), new DittoAnimationFrame(image, TimeSpan.FromMilliseconds(700)) };
        inner = DivoomProtocol.AnimationStream(frames);
    }

    [Test]
    public void AnimationEncodingPreservesDurationsAndWireFormat()
    {
        Assert.That(image.Rgb.Span[0], Is.EqualTo(0), "Image owns a copy");
        var frameSize = BinaryPrimitives.ReadUInt16LittleEndian(inner.AsSpan(1));
        Assert.That(inner[0] == 0xAA, Is.True, "Animation starts directly with AA frame");
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(inner.AsSpan(3)) == 150 &&
            BinaryPrimitives.ReadUInt16LittleEndian(inner.AsSpan(3 + frameSize)) == 700, Is.True, "Per-frame durations");
        Assert.That(inner[5] == 0 && inner.Length == 2 * frameSize, Is.True, "Animation flags and length");
        Assert.That(Convert.ToHexString(DivoomProtocol.AnimationAnnounce(258)) == "8B0002010000", Is.True, "Announce file size golden vector");
        Assert.That(Convert.ToHexString(DivoomProtocol.AnimationChunk(258, 1, [0xAA, 0xBB])) == "FEEFAA550D00008B01020100000100AABB0202", Is.True, "Upload file size and uint16 index golden vector");
    }

    private static DittoProClientOptions Options() => new()
    {
        ControllerId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), TimeProvider = new FixedClock(),
        TimeZone = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test", "test"),
        ChunkDelay = TimeSpan.Zero, CommandDelay = TimeSpan.Zero, InitializationPause = TimeSpan.Zero,
        AcknowledgementTimeout = TimeSpan.FromMilliseconds(30)
    };

    [Test]
    public async Task OperationsReuseSessionAndDisposalIsIdempotent()
    {
        var transport = new FakeTransport { MaxWriteSize = 20 };
        var settings = Options();
        await using (IDittoProClient client = new DittoProClient(transport, settings))
        {
            Assert.That(transport.ConnectCount == 0, Is.True, "Construction does not connect");
            await client.SendImageAsync(image);
            Assert.That(transport.IsConnected && !transport.Disposed && transport.DisconnectCount == 0, Is.True, "Successful operation keeps connection open");
            await client.SendImageAsync(image);
            await client.SendCurrentDateTimeAsync(); await client.SendAnimationAsync(frames);
            var beforeClock = transport.Packets.Count;
            await client.ShowClockAsync();
            Assert.That(transport.Packets.Count == beforeClock + 1 && transport.Packets[^1].AsSpan(11, 11).SequenceEqual(new byte[] { 0x45, 0x00, 1, 0, 1, 0, 0, 0, 0, 255, 0 }), Is.True, "Clock selects style zero in green without syncing time or changing startup settings");
            Assert.That(((FixedClock)settings.TimeProvider).Calls == 2, Is.True, "Current time sampled at initialization and explicit time request");
            Assert.That(transport.ConnectCount == 1 && transport.Packets.Count(p => p[11] == 0x7B) == 1 && transport.IsConnected, Is.True, "Operations reuse one initialized connection");
            using var json = JsonDocument.Parse(transport.Packets[0].AsMemory(11, transport.Packets[0].Length - 13));
            Assert.That(json.RootElement.GetProperty("Time").GetString() == "2026-10-04 22:30:00", Is.True, "Configured time zone");
            Assert.That(json.RootElement.GetProperty("Utc").GetInt64() == new FixedClock().GetUtcNow().ToUnixTimeSeconds(), Is.True, "UTC epoch");
            var date = transport.Packets.Last(p => !(p[6] == 0 && p[7] == 0x8B) && p[11] == 0x18);
            Assert.That(date.AsSpan(11, 9).SequenceEqual(new byte[] { 0x18, 26, 20, 10, 4, 22, 30, 0, 0 }), Is.True, "Date/time wire fields: year remainder + century, not ISO week");
            var preambles = transport.Packets.Where(p => p[11] == 0xBD && p[12] == 0x31).ToArray();
            Assert.That(preambles[0][13] == 0 && preambles[1][13] == 3, Is.True, "Image slot advances within reused session");
            var upload = transport.Packets.Where(p => p[6] == 0 && p[7] == 0x8B).ToArray();
            var announceIndex = transport.Packets.FindIndex(p => p[6] != 0 && p[11] == 0x8B);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(transport.Packets[announceIndex].AsSpan(13)) == inner.Length, Is.True, "Announce contains exact file size");
            Assert.That(upload.Length == 2 && BinaryPrimitives.ReadUInt16LittleEndian(upload[0].AsSpan(13)) == 0 &&
                BinaryPrimitives.ReadUInt16LittleEndian(upload[1].AsSpan(13)) == 1, Is.True, "Upload chunk indices; no invented commit");
            Assert.That(upload.All(p => BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(9)) == inner.Length), Is.True, "Each chunk contains exact file size");
            Assert.That(upload.SelectMany(p => p[15..^2]).SequenceEqual(inner), Is.True, "Reassembled upload is exactly the animation file");
            Assert.That(transport.Writes.All(w => w.Length <= 20), Is.True, "Small negotiated MTU respected");
            await Task.WhenAll(client.SendImageAsync(image), client.SendCurrentDateTimeAsync());
            Assert.That(transport.ConnectCount == 1 && transport.IsConnected && transport.DisconnectCount == 0, Is.True, "Concurrent operations serialize on the shared session");
            using var cancelledRequest = new CancellationTokenSource(); cancelledRequest.Cancel();
            await Assert.ThatAsync(async () => await client.SendImageAsync(image, cancelledRequest.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(transport.IsConnected && transport.DisconnectCount == 0, Is.True, "Cancellation before operation starts preserves established session");
            var beforeCancelledClock = transport.Packets.Count;
            await Assert.ThatAsync(async () => await client.ShowClockAsync(cancelledRequest.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(transport.Packets.Count == beforeCancelledClock && transport.IsConnected, Is.True, "Cancelled clock selection sends nothing and preserves the session");
            await client.DisposeAsync();
            Assert.That(transport.DisconnectCount == 1, Is.True, "Client disposal delegates to transport disposal without a duplicate disconnect");
            var disconnected = transport.DisconnectCount;
            await client.DisposeAsync();
            Assert.That(transport.Disposed && !transport.IsConnected && transport.DisconnectCount == disconnected, Is.True, "Dispose disconnects and is idempotent");
            await Assert.ThatAsync(async () => await client.SendImageAsync(image), Throws.InstanceOf<ObjectDisposedException>());
        }
        Assert.That(transport.Disposed && !transport.IsConnected, Is.True, "Transport disposed by client");
    }

    [Test]
    public async Task ClockAppearanceUsesRgbBytesAndValidatesBeforeConnecting()
    {
        var transport = new FakeTransport();
        await using IDittoProClient client = new DittoProClient(transport, Options());
        Assert.Throws<ArgumentOutOfRangeException>(() => client.ShowClockAsync(16, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.ShowClockAsync(0, 0x1000000));
        Assert.That(transport.ConnectCount, Is.Zero);
        await client.ShowClockAsync(15, 0x123456);
        Assert.That(transport.Packets[^1].AsSpan(11, 11).ToArray(),
            Is.EqualTo(new byte[] { 0x45, 0, 1, 15, 1, 0, 0, 0, 0x12, 0x34, 0x56 }));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var count = transport.Packets.Count;
        await Assert.ThatAsync(async () => await client.ShowClockAsync(1, 0, cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(transport.Packets.Count, Is.EqualTo(count));
        Assert.That(transport.IsConnected, Is.True);
    }

    [Test]
    public async Task LostConnectionReconnectsAndReinitializes()
    {
        var dropped = new FakeTransport();
        await using (IDittoProClient client = new DittoProClient(dropped, Options()))
        {
            await client.SendImageAsync(image);
            await dropped.DisconnectAsync(); // Simulate a remote connection loss.
            await client.SendImageAsync(image);
            Assert.That(dropped.ConnectCount == 2 && dropped.Packets.Count(p => p[11] == 0x7B) == 2 && dropped.IsConnected, Is.True, "Lost connection reconnects and reinitializes");
        }
    }

    [Test]
    public async Task ClockRetriesTransientTransportFailures()
    {
        var transient = new FakeTransport { ClockFailuresRemaining = 2 };
        await using (var client = new DittoProClient(transient, Options()))
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            await client.ShowClockAsync();
            Assert.That(transient.ConnectCount == 3 && transient.DisconnectCount == 2 && transient.IsConnected, Is.True, "Third clock attempt succeeds after two transport failures");
            Assert.That(elapsed.Elapsed >= TimeSpan.FromSeconds(2), Is.True, "Retry waits one second between attempts");
        }
    }

    [Test]
    public async Task ConnectionFailureIsRetried()
    {
        var connectFailure = new FakeTransport { ConnectFailuresRemaining = 1 };
        await using (var client = new DittoProClient(connectFailure, Options()))
        {
            await client.ShowClockAsync();
            Assert.That(connectFailure.ConnectCount == 2 && connectFailure.IsConnected, Is.True, "Connection failure is retried");
        }
    }

    [Test]
    public async Task PartialAnimationRestartsWithCompleteStream()
    {
        var animationFailure = new FakeTransport { UploadFailuresRemaining = 1 };
        await using (var client = new DittoProClient(animationFailure, Options()))
        {
            await client.SendAnimationAsync(frames);
            Assert.That(animationFailure.ConnectCount == 2 && animationFailure.Packets.Count(p => p[6] != 0 && p[11] == 0x8B) == 2, Is.True, "Partial animation restarts with a new announce and session");
            var lastUpload = animationFailure.Packets.FindLastIndex(p => p[6] != 0 && p[11] == 0x8B);
            Assert.That(animationFailure.Packets.Skip(lastUpload + 1).SelectMany(p => p[15..^2]).SequenceEqual(inner), Is.True, "Retried animation resends the complete stream");
        }
    }

    [Test]
    public async Task CancellationDuringRetryPreventsAnotherConnection()
    {
        using (var cancelRetry = new CancellationTokenSource())
        {
            var transportFailure = new FakeTransport { ClockFailuresRemaining = 2 };
            var cancelOptions = OptionsWithRetryCancellation(cancelRetry);
            await using var client = new DittoProClient(transportFailure, cancelOptions);
            await Assert.ThatAsync(async () => await client.ShowClockAsync(cancelRetry.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(transportFailure.ConnectCount == 1 && !transportFailure.IsConnected, Is.True, "Cancellation during retry delay prevents another connection");
        }
    }

    [Test]
    public async Task ClockTimeoutDisconnectsAndNextRequestReconnects()
    {
        var clockTimeout = new FakeTransport { SuppressClockAcknowledgement = true };
        await using (var client = new DittoProClient(clockTimeout, Options()))
        {
            await Assert.ThatAsync(async () => await client.ShowClockAsync(), Throws.InstanceOf<TimeoutException>());
            Assert.That(!clockTimeout.IsConnected, Is.True, "Clock timeout disconnects uncertain session");
            clockTimeout.SuppressClockAcknowledgement = false;
            await client.ShowClockAsync();
            Assert.That(clockTimeout.ConnectCount == 4 && clockTimeout.IsConnected, Is.True, "Clock uses three attempts, then reconnects on the next request");
        }
    }

    [Test]
    public async Task ImageTimeoutDisconnectsAndNextRequestReconnects()
    {
        var retry = new FakeTransport { SuppressImageAcknowledgement = true };
        await using (var client = new DittoProClient(retry, Options()))
        {
            await Assert.ThatAsync(async () => await client.SendImageAsync(image), Throws.InstanceOf<TimeoutException>());
            Assert.That(!retry.IsConnected, Is.True, "Timeout disconnects uncertain session");
            retry.SuppressImageAcknowledgement = false; await client.SendImageAsync(image);
            Assert.That(retry.ConnectCount == 4 && retry.Packets.Count(p => p[11] == 0x7B) == 4, Is.True, "Three failed attempts and the next request each initialize a new session");
        }
    }

    [Test]
    public async Task DeviceRejectsTimeCommand()
    {
        await using (var client = new DittoProClient(new FakeTransport { RejectTime = true }, Options()))
        {
            await Assert.ThatAsync(async () => await client.SendCurrentDateTimeAsync(), Throws.InstanceOf<IOException>());
        }
    }

    [Test]
    public async Task AnimationUploadRequiresExplicitReadiness()
    {
        foreach (var missingReady in new[] { false, true })
        {
            var denied = new FakeTransport { RejectAnimation = !missingReady, SuppressAnimationReady = missingReady };
            await using var client = new DittoProClient(denied, Options());
            if (missingReady)
                await Assert.ThatAsync(async () => await client.SendAnimationAsync(frames), Throws.InstanceOf<TimeoutException>());
            else
                await Assert.ThatAsync(async () => await client.SendAnimationAsync(frames), Throws.InstanceOf<IOException>());
            Assert.That(!denied.Packets.Any(p => p[6] == 0 && p[7] == 0x8B), Is.True, "No upload before explicit start permission");
        }
    }

    [Test]
    public async Task CancelledOperationDoesNotConnectAndEmptyAnimationIsRejected()
    {
        var cancelled = new FakeTransport();
        await using (var client = new DittoProClient(cancelled, Options()))
        {
            using var source = new CancellationTokenSource(); source.Cancel();
            await Assert.ThatAsync(async () => await client.SendImageAsync(image, source.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(cancelled.ConnectCount == 0, Is.True, "Cancelled operation does not connect");
            await Assert.ThatAsync(async () => await client.SendAnimationAsync([]), Throws.InstanceOf<ArgumentException>());
        }
    }

    [Test]
    public async Task CancelledTransferDisconnectsChannel()
    {
        using (var stopTransfer = new CancellationTokenSource())
        {
            var interrupted = new FakeTransport { ImageWritten = stopTransfer.Cancel };
            await using var client = new DittoProClient(interrupted, Options());
            await Assert.ThatAsync(async () => await client.SendImageAsync(image, stopTransfer.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(!interrupted.IsConnected, Is.True, "Cancelled transfer disconnects channel");
        }
    }

    private static DittoProClientOptions OptionsWithRetryCancellation(CancellationTokenSource cancellation) => new()
    {
        ChunkDelay = TimeSpan.Zero, CommandDelay = TimeSpan.Zero, InitializationPause = TimeSpan.Zero,
        Log = message => { if (message.StartsWith("RETRY:")) cancellation.CancelAfter(50); }
    };

}
