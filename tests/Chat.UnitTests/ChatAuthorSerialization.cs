namespace ActualChat.Chat.UnitTests;

public class ChatAuthorSerialization
{
    [Fact]
    public void BasicTest()
    {
        var ca = new ChatAuthorFull() {
            Name = "Alex",
        };
        ca.PassThroughSystemJsonSerializer().Should().Be(ca);
    }
}
