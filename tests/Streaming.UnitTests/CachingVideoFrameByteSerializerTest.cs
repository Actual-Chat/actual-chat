using ActualChat.Video;
using MessagePack.Resolvers;

namespace ActualChat.Streaming.UnitTests;

// Tests for CachingVideoFrameFormatter / CachingVideoFrameResolver — exercises the default
// MessagePack path since the resolver is registered at module init.
public class CachingVideoFrameByteSerializerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly IByteSerializer Serializer = Serializers.MessagePack;

    [Fact]
    public void SingleFrame_RoundTrip()
    {
        var frame = MakeFrame(isKey: true, index: 42);

        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, frame, typeof(VideoFrame));

        var decoded = (VideoFrame)Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame), out var readLength)!;
        readLength.Should().Be(buffer.WrittenCount);
        AssertEqual(decoded, frame);
    }

    [Fact]
    public void SingleFrame_CachePopulatedAfterSerialize()
    {
        var frame = MakeFrame(isKey: false, index: 7);

        frame.SerializedData.IsEmpty.Should().BeTrue();
        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, frame, typeof(VideoFrame));

        frame.SerializedData.IsEmpty.Should().BeFalse("first Serialize should populate the cache");

        // Second Serialize must emit cached bytes verbatim
        var buffer2 = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer2, frame, typeof(VideoFrame));
        buffer2.WrittenMemory.Span.SequenceEqual(buffer.WrittenMemory.Span).Should().BeTrue();
    }

    [Fact]
    public void SingleFrame_CachePopulatedAfterDeserialize()
    {
        // Serialize a "raw" frame without populating cache (by using an uncached copy)
        var source = MakeFrame(isKey: true, index: 3);
        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, source, typeof(VideoFrame));

        var decoded = (VideoFrame)Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame), out _)!;
        decoded.SerializedData.IsEmpty.Should().BeFalse("Deserialize should populate the cache");

        // Re-serializing decoded should use the cached bytes (zero allocation on happy path)
        var buffer2 = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer2, decoded, typeof(VideoFrame));
        buffer2.WrittenMemory.Span.SequenceEqual(decoded.SerializedData.Span).Should().BeTrue();
    }

    [Fact]
    public void CrossCompat_OurFormatterWrite_StandardResolverRead()
    {
        // Guards against key-name or field-shape drift from MessagePack source-gen.
        // Our hand-written formatter must emit bytes the auto-generated formatter can decode.
        var frame = MakeFrame(isKey: true, index: 5);

        var buffer = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        var writer = new MessagePackWriter(buffer);
        CachingVideoFrameFormatter.Instance.Serialize(ref writer, frame, MessagePackSerializerOptions.Standard);
        writer.Flush();

        // Deserialize via StandardResolver (the auto-gen formatter) — must round-trip all fields.
        var standardFormatter = StandardResolver.Instance.GetFormatterWithVerify<VideoFrame>();
        var reader = new MessagePackReader(buffer.WrittenMemory);
        var decoded = standardFormatter.Deserialize(ref reader, MessagePackSerializerOptions.Standard);

        AssertEqual(decoded, frame);
    }

    [Fact]
    public void CrossCompat_StandardResolverWrite_OurFormatterRead()
    {
        // Reverse direction: auto-gen formatter's output must be parseable by our formatter.
        var frame = MakeFrame(isKey: false, index: 11);

        var standardFormatter = StandardResolver.Instance.GetFormatterWithVerify<VideoFrame>();
        var buffer = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        var writer = new MessagePackWriter(buffer);
        standardFormatter.Serialize(ref writer, frame, MessagePackSerializerOptions.Standard);
        writer.Flush();

        var reader = new MessagePackReader(buffer.WrittenMemory);
        var decoded = CachingVideoFrameFormatter.Instance.Deserialize(ref reader, MessagePackSerializerOptions.Standard)!;

        AssertEqual(decoded, frame);
    }

    [Fact]
    public void Deserialize_DataIsAliasedToSerializedData()
    {
        // Regression guard: after deserialize, VideoFrame.Data must NOT be a separately
        // allocated byte[] — it must slice into SerializedData's backing array.
        // Holding two copies of 10 KB per frame was ~544 MB in the allocation profiler.
        var source = MakeFrame(isKey: true, index: 99);
        var buffer = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        Serializer.Write(buffer, source, typeof(VideoFrame));

        var decoded = (VideoFrame)Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame), out _)!;
        decoded.SerializedData.IsEmpty.Should().BeFalse();
        decoded.Data.Length.Should().Be(source.Data.Length);

        // The Data payload must live INSIDE the SerializedData buffer — check that the
        // backing bytes overlap.
        MemoryMarshal.TryGetArray(decoded.SerializedData, out var cachedSeg).Should().BeTrue();
        MemoryMarshal.TryGetArray(decoded.Data, out var dataSeg).Should().BeTrue();
        dataSeg.Array.Should().BeSameAs(cachedSeg.Array,
            "Data must slice into the SerializedData backing array, not a separate byte[]");
        dataSeg.Offset.Should().BeGreaterThan(cachedSeg.Offset);
        (dataSeg.Offset + dataSeg.Count).Should().BeLessThanOrEqualTo(cachedSeg.Offset + cachedSeg.Count);
    }

    [Fact]
    public void FrameArray_RoundTrip_AllCacheHit()
    {
        var frames = new[] {
            MakeFrame(isKey: true,  index: 0),
            MakeFrame(isKey: false, index: 1),
            MakeFrame(isKey: false, index: 2),
            MakeFrame(isKey: true,  index: 3),
        };
        foreach (var f in frames) {
            var tmp = new ArrayPoolBuffer<byte>(1024, mustClear: false);
            Serializer.Write(tmp, f, typeof(VideoFrame));
            tmp.Dispose();
            f.SerializedData.IsEmpty.Should().BeFalse();
        }

        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, frames, typeof(VideoFrame[]));

        var decoded = (VideoFrame[])Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame[]), out var readLength)!;
        readLength.Should().Be(buffer.WrittenCount);
        decoded.Should().HaveCount(frames.Length);
        for (var i = 0; i < frames.Length; i++)
            AssertEqual(decoded[i], frames[i]);
    }

    [Fact]
    public void FrameArray_RoundTrip_AllCacheMiss()
    {
        var frames = new[] {
            MakeFrame(isKey: true,  index: 0),
            MakeFrame(isKey: false, index: 1),
            MakeFrame(isKey: false, index: 2),
        };
        foreach (var f in frames)
            f.SerializedData.IsEmpty.Should().BeTrue();

        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, frames, typeof(VideoFrame[]));

        foreach (var f in frames)
            f.SerializedData.IsEmpty.Should().BeFalse("cache populated as side-effect");

        var decoded = (VideoFrame[])Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame[]), out var readLength)!;
        readLength.Should().Be(buffer.WrittenCount);
        decoded.Should().HaveCount(frames.Length);
        for (var i = 0; i < frames.Length; i++)
            AssertEqual(decoded[i], frames[i]);
    }

    [Fact]
    public void FrameArray_MixedCacheHitMiss()
    {
        var frames = new[] {
            MakeFrame(isKey: true,  index: 0),
            MakeFrame(isKey: false, index: 1),
            MakeFrame(isKey: false, index: 2),
            MakeFrame(isKey: true,  index: 3),
        };
        // Pre-populate cache for frames 0 and 2 only
        var tmp = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(tmp, frames[0], typeof(VideoFrame));
        tmp.Dispose();
        tmp = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(tmp, frames[2], typeof(VideoFrame));
        tmp.Dispose();
        frames[0].SerializedData.IsEmpty.Should().BeFalse();
        frames[1].SerializedData.IsEmpty.Should().BeTrue();
        frames[2].SerializedData.IsEmpty.Should().BeFalse();
        frames[3].SerializedData.IsEmpty.Should().BeTrue();

        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, frames, typeof(VideoFrame[]));

        var decoded = (VideoFrame[])Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame[]), out var readLength)!;
        readLength.Should().Be(buffer.WrittenCount);
        decoded.Should().HaveCount(frames.Length);
        for (var i = 0; i < frames.Length; i++)
            AssertEqual(decoded[i], frames[i]);
    }

    [Fact]
    public void FrameArray_Empty()
    {
        var frames = Array.Empty<VideoFrame>();

        var buffer = new ArrayPoolBuffer<byte>(16, mustClear: false);
        Serializer.Write(buffer, frames, typeof(VideoFrame[]));

        var decoded = (VideoFrame[])Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame[]), out var readLength)!;
        readLength.Should().Be(buffer.WrittenCount);
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void FrameArray_Large()
    {
        // Enough elements to force Array16 header (>= 16)
        var frames = new VideoFrame[32];
        for (var i = 0; i < frames.Length; i++)
            frames[i] = MakeFrame(isKey: i % 15 == 0, index: i);

        var buffer = new ArrayPoolBuffer<byte>(64 * 1024, mustClear: false);
        Serializer.Write(buffer, frames, typeof(VideoFrame[]));

        var decoded = (VideoFrame[])Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame[]), out _)!;
        decoded.Should().HaveCount(frames.Length);
        for (var i = 0; i < frames.Length; i++)
            AssertEqual(decoded[i], frames[i]);
    }

    // --- helpers ---

    private static VideoFrame MakeFrame(bool isKey, int index)
    {
        var data = new byte[isKey ? 400 : 80];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)((index + i) & 0xFF);
        return new VideoFrame {
            Data = data,
            Offset = TimeSpan.FromMilliseconds(index * 33),
            Duration = TimeSpan.FromMilliseconds(33),
            Index = index,
            KeyFrameIndex = isKey ? index : 0,
            Width = isKey ? 1280 : 0,
            Height = isKey ? 720 : 0,
            LayerCount = 3,
            MaxLayerWidth = isKey ? 1280 : 0,
            MaxLayerHeight = isKey ? 720 : 0,
            Description = isKey ? new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67 } : default,
            Codec = isKey ? "avc1" : null,
            TemporalLayerId = 0,
            ServerArrivedAtTicks = 638_000_000_000_000_000L + index,
        };
    }

    private static void AssertEqual(VideoFrame actual, VideoFrame expected)
    {
        actual.IsKeyFrame.Should().Be(expected.IsKeyFrame);
        actual.Offset.Should().Be(expected.Offset);
        actual.Duration.Should().Be(expected.Duration);
        actual.Index.Should().Be(expected.Index);
        actual.KeyFrameIndex.Should().Be(expected.KeyFrameIndex);
        actual.Width.Should().Be(expected.Width);
        actual.Height.Should().Be(expected.Height);
        actual.LayerCount.Should().Be(expected.LayerCount);
        actual.MaxLayerWidth.Should().Be(expected.MaxLayerWidth);
        actual.MaxLayerHeight.Should().Be(expected.MaxLayerHeight);
        actual.Data.Span.SequenceEqual(expected.Data.Span).Should().BeTrue();
        actual.Description.Span.SequenceEqual(expected.Description.Span).Should().BeTrue();
        actual.Codec.Should().Be(expected.Codec);
        actual.TemporalLayerId.Should().Be(expected.TemporalLayerId);
        actual.ServerArrivedAtTicks.Should().Be(expected.ServerArrivedAtTicks);
    }

    [Fact]
    public void Deserialize_LegacyFrame_MissingServerArrivedAtTicks_DefaultsToZero()
    {
        // Backward-compat: 18-entry maps (pre-ServerArrivedAtTicks) must still parse.
        // We hand-write an 18-entry map omitting the new field and assert the decoder
        // skips it gracefully via the default `reader.Skip()` branch.
        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(18);

        writer.Write("Data");                writer.Write(new byte[] { 0xAA, 0xBB });
        writer.Write("Offset");              writer.Write(TimeSpan.FromMilliseconds(99).Ticks);
        writer.Write("Duration");            writer.Write(TimeSpan.FromMilliseconds(33).Ticks);
        writer.Write("OffsetEpoch");         writer.Write(0);
        writer.Write("Index");               writer.Write(7);
        writer.Write("KeyFrameIndex");       writer.Write(7);
        writer.Write("Width");               writer.Write(640);
        writer.Write("Height");              writer.Write(360);
        writer.Write("LayerId");             writer.Write((byte)0);
        writer.Write("LayerCount");          writer.Write((byte)1);
        writer.Write("MaxLayerWidth");       writer.Write(640);
        writer.Write("MaxLayerHeight");      writer.Write(360);
        writer.Write("TemporalLayerId");     writer.Write((byte)0);
        writer.Write("TemporalLayerCount");  writer.Write((byte)1);
        writer.Write("Codec");               writer.Write("avc1");
        writer.Write("Description");         writer.WriteNil();
        writer.Write("DropTrace");           writer.Write(ReadOnlySpan<byte>.Empty);
        writer.Write("Rotation");            writer.Write((byte)0);
        writer.Flush();

        var reader = new MessagePackReader(buffer.WrittenMemory);
        var decoded = CachingVideoFrameFormatter.Instance.Deserialize(ref reader, MessagePackSerializerOptions.Standard)!;

        decoded.Index.Should().Be(7);
        decoded.ServerArrivedAtTicks.Should().Be(0);
    }

    [Fact]
    public void ServerStamping_InPlace_OverwritesCachedBytes()
    {
        // Mirrors VideoStreamingBackend.ProcessFrames: deserialize a frame from
        // the publisher (ServerArrivedAtTicks=0), stamp it via the in-place
        // helper, and verify the cache stays populated and reads back the stamp.
        var sender = MakeFrame(isKey: true, index: 1);
        sender.ServerArrivedAtTicks = 0;
        var wireBuffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(wireBuffer, sender, typeof(VideoFrame));

        var received = (VideoFrame)Serializer.Read(wireBuffer.WrittenMemory, typeof(VideoFrame), out _)!;
        received.ServerArrivedAtTicks.Should().Be(0);
        received.SerializedData.IsEmpty.Should().BeFalse("Deserialize populates the cache");
        var cachedArrayBefore = MemoryMarshal.TryGetArray(received.SerializedData, out var segBefore)
            ? segBefore.Array
            : null;

        var stamp = 638_500_000_000_000_000L;
        CachingVideoFrameFormatter.StampServerArrived(received, stamp);

        received.ServerArrivedAtTicks.Should().Be(stamp);
        received.SerializedData.IsEmpty.Should().BeFalse("in-place stamp must not invalidate the cache");
        MemoryMarshal.TryGetArray(received.SerializedData, out var segAfter).Should().BeTrue();
        segAfter.Array.Should().BeSameAs(cachedArrayBefore, "the backing byte[] is mutated in place");

        var fanoutBuffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(fanoutBuffer, received, typeof(VideoFrame));

        var fanoutDecoded = (VideoFrame)Serializer.Read(fanoutBuffer.WrittenMemory, typeof(VideoFrame), out _)!;
        fanoutDecoded.ServerArrivedAtTicks.Should().Be(stamp);
    }

    [Fact]
    public void ServerStamping_InPlace_MatchesReSerialize()
    {
        // The in-place overwrite must produce byte-identical output to a fresh
        // re-serialize so consumers observe the same bytes either way.
        var frame = MakeFrame(isKey: false, index: 5);
        var wireBuffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(wireBuffer, frame, typeof(VideoFrame));
        var roundTrip = (VideoFrame)Serializer.Read(wireBuffer.WrittenMemory, typeof(VideoFrame), out _)!;

        var stamp = 638_500_000_000_000_123L;
        CachingVideoFrameFormatter.StampServerArrived(roundTrip, stamp);
        var inPlaceBuffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(inPlaceBuffer, roundTrip, typeof(VideoFrame));

        // Now do the same via cache invalidation to compare.
        var roundTrip2 = (VideoFrame)Serializer.Read(wireBuffer.WrittenMemory, typeof(VideoFrame), out _)!;
        roundTrip2.ServerArrivedAtTicks = stamp;
        roundTrip2.SerializedData = default;
        var rebuiltBuffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(rebuiltBuffer, roundTrip2, typeof(VideoFrame));

        inPlaceBuffer.WrittenMemory.Span.SequenceEqual(rebuiltBuffer.WrittenMemory.Span)
            .Should().BeTrue("inline stamp must produce the same bytes as a rebuild");
    }
}
