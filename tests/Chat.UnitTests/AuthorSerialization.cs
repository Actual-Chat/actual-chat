using ActualChat.Users;

namespace ActualChat.Chat.UnitTests;

public class AuthorSerialization
{
    [Fact]
    public void BasicTest()
    {
        var ca = new AuthorFull(new AuthorId(new ChatId("testChatId"), 0, AssumeValid.Option)) {
            Avatar = new (Symbol.Empty) {
                Name = "Alex",
            },
        };
        ca.PassThroughSystemJsonSerializer().Should().Be(ca);
    }
}
