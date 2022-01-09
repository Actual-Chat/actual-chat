namespace ActualChat.Audio.UnitTests;

public class AudioRecordTest
{
    [Fact]
    public void SessionPropertyTest()
    {
        var r = new AudioRecord("", "chatId", new AudioFormat(), 0);
        r.Session.Should().BeNull();
        r = r with { SessionId = "1234567890abcdef" };
        r.Session.Id.Should().Be(r.SessionId);
        r = r with { SessionId = "1234567890abcdefg" };
        r.Session.Id.Should().Be(r.SessionId);
        r = r with { SessionId = "" };
        r.Session.Should().BeNull();
    }
}
