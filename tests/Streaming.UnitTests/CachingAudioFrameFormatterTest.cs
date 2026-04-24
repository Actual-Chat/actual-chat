using ActualChat.Audio;
using MessagePack.Resolvers;

namespace ActualChat.Streaming.UnitTests;

// Tests for CachingAudioFrameFormatter / CachingAudioFrameResolver — exercises the default
// MessagePack path since the resolver is registered at module init.
public class CachingAudioFrameFormatterTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly MessagePackByteSerializer Serializer = MessagePackByteSerializer.Default;

    [Fact]
    public void SingleFrame_RoundTrip()
    {
        var frame = MakeFrame(index: 42);

        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, frame, typeof(AudioFrame));

        var decoded = (AudioFrame)Serializer.Read(buffer.WrittenMemory, typeof(AudioFrame), out var readLength)!;
        readLength.Should().Be(buffer.WrittenCount);
        AssertEqual(decoded, frame);
    }

    [Fact]
    public void HeaderFrame_RoundTrip()
    {
        // Header frame uses Offset = -1 ms to mark stream start, per AudioStreamingBackend.ProcessAudio.
        var frame = new AudioFrame {
            Data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            Offset = TimeSpan.FromMilliseconds(-1),
            Duration = TimeSpan.FromMilliseconds(20),
            IsKeyFrame = true,
        };

        var buffer = new ArrayPoolBuffer<byte>(256, mustClear: false);
        Serializer.Write(buffer, frame, typeof(AudioFrame));

        var decoded = (AudioFrame)Serializer.Read(buffer.WrittenMemory, typeof(AudioFrame), out _)!;
        AssertEqual(decoded, frame);
    }

    [Fact]
    public void SingleFrame_CachePopulatedAfterSerialize()
    {
        var frame = MakeFrame(index: 7);

        frame.SerializedData.IsEmpty.Should().BeTrue();
        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, frame, typeof(AudioFrame));

        frame.SerializedData.IsEmpty.Should().BeFalse("first Serialize should populate the cache");

        // Second Serialize must emit cached bytes verbatim
        var buffer2 = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer2, frame, typeof(AudioFrame));
        buffer2.WrittenMemory.Span.SequenceEqual(buffer.WrittenMemory.Span).Should().BeTrue();
    }

    [Fact]
    public void SingleFrame_CachePopulatedAfterDeserialize()
    {
        var source = MakeFrame(index: 3);
        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, source, typeof(AudioFrame));

        var decoded = (AudioFrame)Serializer.Read(buffer.WrittenMemory, typeof(AudioFrame), out _)!;
        decoded.SerializedData.IsEmpty.Should().BeFalse("Deserialize should populate the cache");

        // Re-serializing decoded should use the cached bytes verbatim
        var buffer2 = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer2, decoded, typeof(AudioFrame));
        buffer2.WrittenMemory.Span.SequenceEqual(decoded.SerializedData.Span).Should().BeTrue();
    }

    [Fact]
    public void CrossCompat_OurFormatterWrite_StandardResolverRead()
    {
        // Our hand-written formatter must emit bytes the auto-generated formatter can decode.
        var frame = MakeFrame(index: 5);

        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        var writer = new MessagePackWriter(buffer);
        CachingAudioFrameFormatter.Instance.Serialize(ref writer, frame, MessagePackSerializerOptions.Standard);
        writer.Flush();

        var standardFormatter = StandardResolver.Instance.GetFormatterWithVerify<AudioFrame>();
        var reader = new MessagePackReader(buffer.WrittenMemory);
        var decoded = standardFormatter.Deserialize(ref reader, MessagePackSerializerOptions.Standard);

        AssertEqual(decoded, frame);
    }

    [Fact]
    public void CrossCompat_StandardResolverWrite_OurFormatterRead()
    {
        // Reverse direction: auto-gen formatter's output must be parseable by our formatter.
        var frame = MakeFrame(index: 11);

        var standardFormatter = StandardResolver.Instance.GetFormatterWithVerify<AudioFrame>();
        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        var writer = new MessagePackWriter(buffer);
        standardFormatter.Serialize(ref writer, frame, MessagePackSerializerOptions.Standard);
        writer.Flush();

        var reader = new MessagePackReader(buffer.WrittenMemory);
        var decoded = CachingAudioFrameFormatter.Instance.Deserialize(ref reader, MessagePackSerializerOptions.Standard);

        AssertEqual(decoded, frame);
    }

    [Fact]
    public void Deserialize_DataIsAliasedToSerializedData()
    {
        // Regression guard: after deserialize, AudioFrame.Data must NOT be a separately
        // allocated byte[] — it must slice into SerializedData's backing array.
        var source = MakeFrame(index: 99);
        var buffer = new ArrayPoolBuffer<byte>(1024, mustClear: false);
        Serializer.Write(buffer, source, typeof(AudioFrame));

        var decoded = (AudioFrame)Serializer.Read(buffer.WrittenMemory, typeof(AudioFrame), out _)!;
        decoded.SerializedData.IsEmpty.Should().BeFalse();
        decoded.Data.Length.Should().Be(source.Data.Length);

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
            MakeFrame(index: 0),
            MakeFrame(index: 1),
            MakeFrame(index: 2),
            MakeFrame(index: 3),
        };
        // Pre-populate cache
        foreach (var f in frames) {
            var tmp = new ArrayPoolBuffer<byte>(512, mustClear: false);
            Serializer.Write(tmp, f, typeof(AudioFrame));
            tmp.Dispose();
            f.SerializedData.IsEmpty.Should().BeFalse();
        }

        var buffer = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        Serializer.Write(buffer, frames, typeof(AudioFrame[]));

        var decoded = (AudioFrame[])Serializer.Read(buffer.WrittenMemory, typeof(AudioFrame[]), out var readLength)!;
        readLength.Should().Be(buffer.WrittenCount);
        decoded.Should().HaveCount(frames.Length);
        for (var i = 0; i < frames.Length; i++)
            AssertEqual(decoded[i], frames[i]);
    }

    [Fact]
    public void FrameArray_RoundTrip_AllCacheMiss()
    {
        var frames = new[] {
            MakeFrame(index: 0),
            MakeFrame(index: 1),
            MakeFrame(index: 2),
        };
        foreach (var f in frames)
            f.SerializedData.IsEmpty.Should().BeTrue();

        var buffer = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        Serializer.Write(buffer, frames, typeof(AudioFrame[]));

        foreach (var f in frames)
            f.SerializedData.IsEmpty.Should().BeFalse("cache populated as side-effect");

        var decoded = (AudioFrame[])Serializer.Read(buffer.WrittenMemory, typeof(AudioFrame[]), out _)!;
        decoded.Should().HaveCount(frames.Length);
        for (var i = 0; i < frames.Length; i++)
            AssertEqual(decoded[i], frames[i]);
    }

    [Fact]
    public void FrameArray_Empty()
    {
        var frames = Array.Empty<AudioFrame>();

        var buffer = new ArrayPoolBuffer<byte>(16, mustClear: false);
        Serializer.Write(buffer, frames, typeof(AudioFrame[]));

        var decoded = (AudioFrame[])Serializer.Read(buffer.WrittenMemory, typeof(AudioFrame[]), out _)!;
        decoded.Should().BeEmpty();
    }

    // --- helpers ---

    private static AudioFrame MakeFrame(int index)
    {
        // Opus frames are typically ~80-200 bytes at 32 kbps/20ms.
        var data = new byte[120];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)((index + i) & 0xFF);
        return new AudioFrame {
            Data = data,
            Offset = TimeSpan.FromMilliseconds(index * 20),
            Duration = Constants.Audio.OpusFrameDuration,
            IsKeyFrame = true,
        };
    }

    private static void AssertEqual(AudioFrame actual, AudioFrame expected)
    {
        actual.Offset.Should().Be(expected.Offset);
        actual.Duration.Should().Be(expected.Duration);
        actual.IsKeyFrame.Should().Be(expected.IsKeyFrame);
        actual.Data.Span.SequenceEqual(expected.Data.Span).Should().BeTrue();
    }
}
