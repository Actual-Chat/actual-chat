using ActualChat.Audio;
using ActualChat.Video;

namespace ActualChat.Streaming.UnitTests;

// Round-trip tests for VideoFrame[] / AudioFrame[] via the keyless MessagePack serializer.
// The keyless resolver's AttributeFormatterResolver picks up [MessagePackFormatter] — both
// frame types therefore encode via their hand-written Caching*FrameFormatter under keyless
// too. These tests guard that path.
public class KeylessFrameSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly IByteSerializer Serializer = Serializers.KeylessMessagePack;

    [Fact]
    public void VideoFrameArray_RoundTrip()
    {
        var frames = new[] {
            MakeVideoFrame(isKey: true,  index: 0),
            MakeVideoFrame(isKey: false, index: 1),
            MakeVideoFrame(isKey: false, index: 2),
            MakeVideoFrame(isKey: true,  index: 3),
        };

        var buffer = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        Serializer.Write(buffer, frames, typeof(VideoFrame[]));
        var json = MessagePackSerializer.ConvertToJson(buffer.WrittenMemory);
        json.Should().Contain("Width").And.Contain("Height");

        var decoded = (VideoFrame[])Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame[]), out var readLength)!;
        readLength.Should().Be(buffer.WrittenCount);
        decoded.Should().HaveCount(frames.Length);
        for (var i = 0; i < frames.Length; i++)
            AssertEqual(decoded[i], frames[i]);
    }

    [Fact]
    public void VideoFrameArray_Empty_RoundTrip()
    {
        var frames = Array.Empty<VideoFrame>();

        var buffer = new ArrayPoolBuffer<byte>(32, mustClear: false);
        Serializer.Write(buffer, frames, typeof(VideoFrame[]));

        var decoded = (VideoFrame[])Serializer.Read(buffer.WrittenMemory, typeof(VideoFrame[]), out _)!;
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void AudioFrameArray_RoundTrip()
    {
        var frames = new[] {
            MakeAudioFrame(index: 0),
            MakeAudioFrame(index: 1),
            MakeAudioFrame(index: 2),
            MakeAudioFrame(index: 3),
        };

        var buffer = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        Serializer.Write(buffer, frames, typeof(AudioFrame[]));
        var json = MessagePackSerializer.ConvertToJson(buffer.WrittenMemory);
        json.Should().Contain("Offset").And.Contain("Duration");

        var decoded = (AudioFrame[])Serializer.Read(buffer.WrittenMemory, typeof(AudioFrame[]), out var readLength)!;
        readLength.Should().Be(buffer.WrittenCount);
        decoded.Should().HaveCount(frames.Length);
        for (var i = 0; i < frames.Length; i++)
            AssertEqual(decoded[i], frames[i]);
    }

    [Fact]
    public void AudioFrameArray_Empty_RoundTrip()
    {
        var frames = Array.Empty<AudioFrame>();

        var buffer = new ArrayPoolBuffer<byte>(32, mustClear: false);
        Serializer.Write(buffer, frames, typeof(AudioFrame[]));

        var decoded = (AudioFrame[])Serializer.Read(buffer.WrittenMemory, typeof(AudioFrame[]), out _)!;
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void VideoFrameArray_Keyless_Matches_Default_Bytes()
    {
        // CachingVideoFrameFormatter is picked up via [MessagePackFormatter] under both the
        // default and keyless chains, so the wire format must match byte-for-byte.
        var frames = new[] {
            MakeVideoFrame(isKey: true,  index: 10),
            MakeVideoFrame(isKey: false, index: 11),
        };

        var keylessBuf = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        Serializer.Write(keylessBuf, frames, typeof(VideoFrame[]));

        // Clear SerializedData cache so default path re-serializes from scratch, not from cache.
        foreach (var f in frames)
            f.SerializedData = default;

        var defaultBuf = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        Serializers.MessagePack.Write(defaultBuf, frames, typeof(VideoFrame[]));

        keylessBuf.WrittenSpan.SequenceEqual(defaultBuf.WrittenSpan).Should().BeTrue();
    }

    [Fact]
    public void AudioFrameArray_Keyless_Matches_Default_Bytes()
    {
        var frames = new[] {
            MakeAudioFrame(index: 20),
            MakeAudioFrame(index: 21),
        };

        var keylessBuf = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        Serializer.Write(keylessBuf, frames, typeof(AudioFrame[]));

        foreach (var f in frames)
            f.SerializedData = default;

        var defaultBuf = new ArrayPoolBuffer<byte>(4096, mustClear: false);
        Serializers.MessagePack.Write(defaultBuf, frames, typeof(AudioFrame[]));

        keylessBuf.WrittenSpan.SequenceEqual(defaultBuf.WrittenSpan).Should().BeTrue();
    }

    // --- helpers ---

    private static VideoFrame MakeVideoFrame(bool isKey, int index)
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
            Description = isKey ? new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67 } : default,
            Codec = isKey ? "avc1" : null,
            TemporalLayerId = 0,
        };
    }

    private static AudioFrame MakeAudioFrame(int index)
    {
        var data = new byte[120];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)((index + i) & 0xFF);
        return new AudioFrame {
            Data = data,
            Offset = TimeSpan.FromMilliseconds(index * 20),
            Duration = Constants.Audio.OpusFrameDuration,
            LegacyIsKeyFrame = true,
        };
    }

    private static void AssertEqual(VideoFrame actual, VideoFrame expected)
    {
        actual.IsKeyFrame.Should().Be(expected.IsKeyFrame);
        actual.Offset.Should().Be(expected.Offset);
        actual.Duration.Should().Be(expected.Duration);
        actual.Width.Should().Be(expected.Width);
        actual.Height.Should().Be(expected.Height);
        actual.Data.Span.SequenceEqual(expected.Data.Span).Should().BeTrue();
        actual.Description.Span.SequenceEqual(expected.Description.Span).Should().BeTrue();
        actual.Codec.Should().Be(expected.Codec);
        actual.TemporalLayerId.Should().Be(expected.TemporalLayerId);
    }

    private static void AssertEqual(AudioFrame actual, AudioFrame expected)
    {
        actual.Offset.Should().Be(expected.Offset);
        actual.Duration.Should().Be(expected.Duration);
        actual.LegacyIsKeyFrame.Should().Be(expected.LegacyIsKeyFrame);
        actual.Data.Span.SequenceEqual(expected.Data.Span).Should().BeTrue();
    }
}
