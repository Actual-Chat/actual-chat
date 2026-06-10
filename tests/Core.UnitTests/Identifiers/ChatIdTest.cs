namespace ActualChat.Core.UnitTests.Identifiers;

public class ChatIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<ChatId>(@out)
{
    public override string[] ValidIdentifiers => new[] {
            "1234abcd",
            "p-Actual-actual-admin",
            "p-actual-admin-bobby93",
            "p-admin1-admin2",
            "p-admin1-admin2-148",
            "p-123456-admin1",
            "p-123456-admin1-7",
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

    [Fact]
    public void PeerChatThreadIdShouldParse()
    {
        // act
        var chatId = ChatId.Parse("p-admin1-admin2-148");

        // assert
        var threadChatId = chatId.Should().BeOfType<ThreadChatId>().Subject;
        threadChatId.ThreadId.Should().Be(148);
        threadChatId.ParentChatId.Should().BeOfType<PeerChatId>();
        threadChatId.ParentChatId.Value.Should().Be("p-admin1-admin2");
    }

    [Fact]
    public void NumericUserIdPeerChatIdShouldNotBeMisreadAsThread()
    {
        // act
        var chatId = ChatId.Parse("p-123456-admin1");

        // assert
        var peerChatId = chatId.Should().BeOfType<PeerChatId>().Subject;
        peerChatId.UserId1.Should().Be(UserId.Parse("123456"));
        peerChatId.UserId2.Should().Be(UserId.Parse("admin1"));
    }

    [Fact]
    public void NestedPeerChatThreadIdShouldRoundTrip()
    {
        // act
        var chatId = ChatId.Parse("p-admin1-admin2-148-3");

        // assert
        var threadChatId = chatId.Should().BeOfType<ThreadChatId>().Subject;
        threadChatId.ThreadId.Should().Be(3);
        threadChatId.ParentChatId.Value.Should().Be("p-admin1-admin2-148");
        chatId.Value.Should().Be("p-admin1-admin2-148-3");
    }

    [Fact]
    public void RoleIdInPeerChatThreadShouldParse()
    {
        // act
        var roleId = RoleId.Parse("p-i0fuE0-kiLGwd-148:1");

        // assert
        roleId.ChatId.Should().BeOfType<ThreadChatId>();
        roleId.ChatId.Value.Should().Be("p-i0fuE0-kiLGwd-148");
        roleId.LocalId.Should().Be(1);
    }
}
