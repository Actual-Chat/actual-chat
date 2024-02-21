namespace ActualChat.Streaming.UnitTests;

public class AudioRecordTest
{
    [Fact]
    public void SessionPropertyTest()
    {
        var nodeRef = new NodeRef(Generate.Option);
        var streamId = new StreamId(nodeRef, Generate.Option);
        var r = new AudioRecord(streamId, null!, new ChatId("chatId"), 0, ChatEntryId.None);
        r.Session.Should().BeNull();
        r = r with { Session = new Session("1234567890abcdef") };
        r.Session.Should().Be(r.Session);
        r = r with { Session = new Session("1234567890abcdefg") };
        r.Session.Should().Be(r.Session);
        r = r with { Session = null! };
        r.Session.Should().BeNull();
    }
}
