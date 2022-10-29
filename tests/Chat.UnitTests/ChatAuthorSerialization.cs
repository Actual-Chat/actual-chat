using ActualChat.Users;

namespace ActualChat.Chat.UnitTests;

public class ChatAuthorSerialization
{
    [Fact]
    public void BasicTest()
    {
        var ca = new ChatAuthorFull() {
            Avatar = new Avatar() {
                Name = "Alex",
            },
        };
        ca.PassThroughSystemJsonSerializer().Should().Be(ca);
    }
}
