using ActualChat.Live;
using ActualChat.Video;

namespace ActualChat.Streaming.UnitTests;

public class StreamingSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void LiveStreamInfo_Basic()
    {
        var authorId = AuthorId.New(TestChatId, 5);
        var info = new LiveAudioStreamInfo {
            ChatId = TestChatId,
            AuthorId = authorId,
            StreamId = "stream-1",
            BeginsAt = new Moment(DateTime.UtcNow),
        };
        info.AssertPassesThroughSerializers();
    }

    [Fact]
    public void AudioRecord_Basic()
    {
        var session = Session.New();
        var streamId = StreamId.New(NodeRef.Parse("1234abcd"), "local1");
        var record = new AudioRecord(streamId, session, TestChatId, 0.0, null);
        var s = record.PassThroughSerializers(Out);
        s.StreamId.Should().Be(record.StreamId);
        s.ChatId.Should().Be(record.ChatId);
        s.ClientStartAt.Should().Be(record.ClientStartAt);
    }

    // VideoFrameBundle / VideoFrame carry ReadOnlyMemory<byte> payloads (Data,
    // Description) that JSON serializers can't round-trip. The wire formats
    // for these types are MessagePack (RPC) and MemoryPack — exercise both.

    [Fact]
    public void VideoFrameBundle_Empty()
    {
        var bundle = new VideoFrameBundle([]);
        var mp = bundle.PassThroughMessagePackByteSerializer(Out);
        mp.Layers.Should().BeEmpty();
        mp.LayerCount.Should().Be(0);
        var mem = bundle.PassThroughMemoryPackByteSerializer(Out);
        mem.Layers.Should().BeEmpty();
        mem.LayerCount.Should().Be(0);
    }

    [Fact]
    public void VideoFrameBundle_SingleLayer()
    {
        var bundle = new VideoFrameBundle([MakeFrame(isKey: true, layerId: 0, layerCount: 1, index: 0)]);

        var mp = bundle.PassThroughMessagePackByteSerializer(Out);
        mp.LayerCount.Should().Be(1);
        AssertEqual(mp.Layers[0], bundle.Layers[0]);

        var mem = bundle.PassThroughMemoryPackByteSerializer(Out);
        mem.LayerCount.Should().Be(1);
        AssertEqual(mem.Layers[0], bundle.Layers[0]);
    }

    [Fact]
    public void VideoFrameBundle_Simulcast()
    {
        // Simulcast bundle: 3 layers, ordered bottom-first, all sharing capture
        // time and keyframe policy — only Data, dims, Description, LayerId differ.
        var bundle = new VideoFrameBundle([
            MakeFrame(isKey: true, layerId: 0, layerCount: 3, index: 7, width: 320,  height: 180),
            MakeFrame(isKey: true, layerId: 1, layerCount: 3, index: 7, width: 640,  height: 360),
            MakeFrame(isKey: true, layerId: 2, layerCount: 3, index: 7, width: 1280, height: 720),
        ]);

        var mp = bundle.PassThroughMessagePackByteSerializer(Out);
        mp.LayerCount.Should().Be(3);
        for (var i = 0; i < bundle.Layers.Length; i++)
            AssertEqual(mp.Layers[i], bundle.Layers[i]);

        var mem = bundle.PassThroughMemoryPackByteSerializer(Out);
        mem.LayerCount.Should().Be(3);
        for (var i = 0; i < bundle.Layers.Length; i++)
            AssertEqual(mem.Layers[i], bundle.Layers[i]);
    }

    private static VideoFrame MakeFrame(
        bool isKey, byte layerId, byte layerCount, int index, int width = 1280, int height = 720)
    {
        var data = new byte[isKey ? 400 : 80];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)((index + layerId + i) & 0xFF);
        return new VideoFrame {
            Data = data,
            Offset = TimeSpan.FromMilliseconds(index * 33),
            Duration = TimeSpan.FromMilliseconds(33),
            Index = index,
            KeyFrameIndex = isKey ? index : 0,
            Width = isKey ? width : 0,
            Height = isKey ? height : 0,
            Description = isKey ? new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67 } : default,
            Codec = isKey ? "avc1" : null,
            LayerId = layerId,
            LayerCount = layerCount,
            TemporalLayerId = 0,
        };
    }

    private static void AssertEqual(VideoFrame actual, VideoFrame expected)
    {
        actual.IsKeyFrame.Should().Be(expected.IsKeyFrame);
        actual.Offset.Should().Be(expected.Offset);
        actual.Duration.Should().Be(expected.Duration);
        actual.Width.Should().Be(expected.Width);
        actual.Height.Should().Be(expected.Height);
        actual.LayerId.Should().Be(expected.LayerId);
        actual.LayerCount.Should().Be(expected.LayerCount);
        actual.TemporalLayerId.Should().Be(expected.TemporalLayerId);
        actual.Data.Span.SequenceEqual(expected.Data.Span).Should().BeTrue();
        actual.Description.Span.SequenceEqual(expected.Description.Span).Should().BeTrue();
        actual.Codec.Should().Be(expected.Codec);
    }
}
