using ActualChat.Users;

namespace ActualChat.Chat.UnitTests;

public class AuthorSerialization
{
    [Fact]
    public void BasicTest()
    {
        var ca = new AuthorFull(new AuthorId(new ChatId("testChatId"), 0, ParseOptions.Skip)) {
            Avatar = new Avatar() {
                Name = "Alex",
            },
        };
        ca.PassThroughSystemJsonSerializer().Should().Be(ca);
    }
}
