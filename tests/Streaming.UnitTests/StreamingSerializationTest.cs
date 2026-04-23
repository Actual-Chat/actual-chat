using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Media;
using ActualChat.Streaming;
using ActualChat.Video;

namespace ActualChat.Streaming.UnitTests;

public class StreamingSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void LiveStreamInfo_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var info = new LiveStreamInfo {
            ChatId = TestChatId,
            AuthorId = authorId,
            StreamId = "stream-1",
            BeginsAt = new Moment(DateTime.UtcNow),
        };
        info.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void AudioRecord_Basic()
    {
        var session = Session.New();
        var streamId = StreamId.New(NodeRef.Parse("1234abcd"), "local1");
        var record = new AudioRecord(streamId, session, TestChatId, 0.0, null);
        var s = record.PassThroughAllSerializers(Out);
        s.StreamId.Should().Be(record.StreamId);
        s.ChatId.Should().Be(record.ChatId);
        s.ClientStartOffset.Should().Be(record.ClientStartOffset);
    }

    // Guards the AotHelper nested-type emission fix: `LiveAudioBackend.State` is a nested
    // [MemoryPackable] record; an earlier FormatTypeName bug stripped the declaring-type
    // segment and emitted `[GenerateShapeFor<ActualChat.Streaming.State>]` (a non-existent
    // type), so the witness compiled but no shape was generated for the actual nested type.
    [Fact]
    public void LiveAudioBackendState_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var stream = new LiveStreamInfo {
            ChatId = TestChatId,
            AuthorId = authorId,
            StreamId = "stream-nested",
            BeginsAt = new Moment(DateTime.UtcNow),
        };
        var state = new LiveAudioBackend.State(7, ApiArray.New(stream));
        var s = state.PassThroughAllSerializers(Out);
        s.Version.Should().Be(state.Version);
        s.Streams.Should().BeEquivalentTo(state.Streams);
    }

    // Round-trip an AudioFrame through the [Key]-honoring (msgpack6) and keyless (msgpack6ck)
    // wire variants. The keyless path is what TS clients use over the
    // wss://...?f=msgpack6ck connection; if a wire mismatch silently zeros out a field, the
    // streaming pipeline appears to work (no exception) but the player gets an empty frame.
    [Fact]
    public void AudioFrame_RoundTrip()
    {
        var frame = new AudioFrame {
            Data = new byte[] { 1, 2, 3, 4, 5 },
            Offset = TimeSpan.FromMilliseconds(60),
            Duration = TimeSpan.FromMilliseconds(20),
            IsKeyFrame = true,
        };
        // ClientSide = codegen-only (no reflection fallback). Validates that the AOT-client
        // wire path doesn't silently miss a shape and ship a default-populated frame.
        AssertAudioFrameRoundTrip(frame, Serializers.ClientSide.MessagePack);
        AssertAudioFrameRoundTrip(frame, Serializers.ClientSide.KeylessMessagePack);
    }

    // Round-trip a VideoFrame through both wire variants. VideoFrame extends MediaFrame with
    // Width/Height/Description/Codec/TemporalLayerId; Description is a ReadOnlyMemory<byte>
    // populated only on keyframes (carries SPS/PPS-style codec config). A missing
    // ReadOnlyMemory<byte> shape on the witness-local provider would silently default
    // Description to empty — which the decoder rejects as malformed first frame.
    [Fact]
    public void VideoFrame_KeyFrame_RoundTrip()
    {
        var frame = new VideoFrame(isKeyFrame: true) {
            Data = new byte[] { 0x10, 0x20, 0x30, 0x40 },
            Offset = TimeSpan.FromMilliseconds(0),
            Duration = TimeSpan.FromMilliseconds(33),
            Width = 1280,
            Height = 720,
            Description = new byte[] { 0xAA, 0xBB, 0xCC },
            Codec = "av01.0.05M.08",
            TemporalLayerId = 0,
        };
        AssertVideoFrameRoundTrip(frame, Serializers.ClientSide.MessagePack);
        AssertVideoFrameRoundTrip(frame, Serializers.ClientSide.KeylessMessagePack);
    }

    [Fact]
    public void VideoFrame_NonKeyFrame_RoundTrip()
    {
        var frame = new VideoFrame(isKeyFrame: false) {
            Data = new byte[] { 0x01, 0x02, 0x03 },
            Offset = TimeSpan.FromMilliseconds(33),
            Duration = TimeSpan.FromMilliseconds(33),
            Width = 1280,
            Height = 720,
            Description = ReadOnlyMemory<byte>.Empty,
            Codec = null,
            TemporalLayerId = 1,
        };
        AssertVideoFrameRoundTrip(frame, Serializers.ClientSide.MessagePack);
        AssertVideoFrameRoundTrip(frame, Serializers.ClientSide.KeylessMessagePack);
    }

    // MediaFrame's [DerivedTypeShape] union must dispatch to the correct subtype on read.
    // When the frame is typed as the abstract base on the wire (which happens whenever
    // RpcStream<MediaFrame> or any polymorphic arg is involved), failing to round-trip the
    // discriminator produces null or the wrong subtype. Keyless mode in particular treats
    // [Key] as advisory, so the union tag layout must work without it.
    [Fact]
    public void MediaFrame_UnionDispatch_RoundTrip()
    {
        var audio = (MediaFrame)new AudioFrame {
            Data = new byte[] { 1, 2, 3 },
            Offset = TimeSpan.FromMilliseconds(20),
        };
        var video = (MediaFrame)new VideoFrame(isKeyFrame: true) {
            Data = new byte[] { 9, 9, 9 },
            Width = 640,
            Height = 480,
            Codec = "vp9",
        };
        AssertUnionRoundTrip<AudioFrame>(audio, Serializers.ClientSide.MessagePack);
        AssertUnionRoundTrip<AudioFrame>(audio, Serializers.ClientSide.KeylessMessagePack);
        AssertUnionRoundTrip<VideoFrame>(video, Serializers.ClientSide.MessagePack);
        AssertUnionRoundTrip<VideoFrame>(video, Serializers.ClientSide.KeylessMessagePack);
    }

    // Private helpers

    private void AssertAudioFrameRoundTrip(AudioFrame original, IByteSerializer serializer)
    {
        var bytes = serializer.Write(original).WrittenSpan.ToArray();
        Out.WriteLine($"{serializer.GetType().Name}: {bytes.Length} bytes");
        var s = (AudioFrame)serializer.Read(bytes, typeof(AudioFrame), out _)!;
        s.Should().NotBeNull();
        s.Data.ToArray().Should().Equal(original.Data.ToArray());
        s.Offset.Should().Be(original.Offset);
        s.Duration.Should().Be(original.Duration);
        s.IsKeyFrame.Should().Be(original.IsKeyFrame);
    }

    private void AssertVideoFrameRoundTrip(VideoFrame original, IByteSerializer serializer)
    {
        var bytes = serializer.Write(original).WrittenSpan.ToArray();
        Out.WriteLine($"{serializer.GetType().Name}: {bytes.Length} bytes");
        var s = (VideoFrame)serializer.Read(bytes, typeof(VideoFrame), out _)!;
        s.Should().NotBeNull();
        s.Data.ToArray().Should().Equal(original.Data.ToArray());
        s.Offset.Should().Be(original.Offset);
        s.Duration.Should().Be(original.Duration);
        s.IsKeyFrame.Should().Be(original.IsKeyFrame);
        s.Width.Should().Be(original.Width);
        s.Height.Should().Be(original.Height);
        s.Description.ToArray().Should().Equal(original.Description.ToArray());
        s.Codec.Should().Be(original.Codec);
        s.TemporalLayerId.Should().Be(original.TemporalLayerId);
    }

    private void AssertUnionRoundTrip<TExpected>(MediaFrame original, IByteSerializer serializer)
        where TExpected : MediaFrame
    {
        var bytes = serializer.Write(original, typeof(MediaFrame)).WrittenSpan.ToArray();
        Out.WriteLine($"{serializer.GetType().Name} (union): {bytes.Length} bytes");
        var s = (MediaFrame)serializer.Read(bytes, typeof(MediaFrame), out _)!;
        s.Should().NotBeNull();
        s.Should().BeOfType<TExpected>();
        s.Data.ToArray().Should().Equal(original.Data.ToArray());
    }
}
