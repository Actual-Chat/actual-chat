namespace ActualChat.Audio.UnitTests;

public class AudioRecordTest
{
    [Fact]
    public void SessionPropertyTest()
    {
        var r = AudioRecord.New(null!, new ChatId("chatId"), 0, ChatEntryId.None);
        r.Session.Should().BeNull();
        r = r with { Session = new Session("1234567890abcdef") };
        r.Session.Should().Be(r.Session);
        r = r with { Session = new Session("1234567890abcdefg") };
        r.Session.Should().Be(r.Session);
        r = r with { Session = null! };
        r.Session.Should().BeNull();
    }
}
