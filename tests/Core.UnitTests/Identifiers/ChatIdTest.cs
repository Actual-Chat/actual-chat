namespace ActualChat.Core.UnitTests.Identifiers;

public class ChatIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<ChatId>(@out)
{
    public override string[] ValidIdentifiers => new[] {
            "1234abcd",
            "p-Actual-actual-admin",
            "p-actual-admin-bobby93",
            "p-admin1-admin2",
            "p-bobby93-ml-search",
            "whatever",
        }
        .Concat(Constants.Chat.SystemChatIdValues)
        .ToArray();
    public override string[] InvalidIdentifiers => [
        "x",
        "p-actual-admin-actual-admin",
        "p-bobby93-actual-admin",
        "p-ml-search-ml-search",
        "p-ml-search-bobby93",
        "some:chat",
        "peer-chat",
        "~guest~1",
    ];

    [Fact]
    public void PeerChatIdWithLegacyUserIdShouldParse()
    {
        // act
        var chatId = ChatId.Parse("p-actual-admin-bobby93");

        // assert
        var peerChatId = chatId.Should().BeOfType<PeerChatId>().Subject;
        peerChatId.UserId1.Should().Be(UserId.Parse("actual-admin"));
        peerChatId.UserId2.Should().Be(UserId.Parse("bobby93"));
    }

    [Fact]
    public void PeerChatIdWithLegacyMlSearchUserIdShouldParse()
    {
        // act
        var chatId = ChatId.Parse("p-bobby93-ml-search");

        // assert
        var peerChatId = chatId.Should().BeOfType<PeerChatId>().Subject;
        peerChatId.UserId1.Should().Be(UserId.Parse("bobby93"));
        peerChatId.UserId2.Should().Be(UserId.Parse("ml-search"));
    }
}
