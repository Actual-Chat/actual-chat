using ActualChat.Users;

namespace ActualChat.Chat.UnitTests;

public class AuthorSerialization
{
    [Fact]
    public void BasicTest()
    {
        var ca = new AuthorFull() {
            Avatar = new Avatar() {
                Name = "Alex",
            },
        };
        ca.PassThroughSystemJsonSerializer().Should().Be(ca);
    }
}
