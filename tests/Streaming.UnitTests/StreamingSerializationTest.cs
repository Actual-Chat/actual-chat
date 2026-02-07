using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Streaming;

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
}
