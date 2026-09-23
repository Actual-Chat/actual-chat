using ActualChat.Testing.Host;
using ModelContextProtocol.Client;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class MultiUserAccessTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task TwoUsers_AccessOwnAndMutualChatsOnly()
    {
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");

        var bob = await Tester.SignInAsUniqueBob();
        var bobKey = await IssueApiKey("bob");

        var (bobPrivateChatId, _) = await Tester.CreateChat(isPublicChat: false, title: "BobPrivate");

        await Tester.SignIn(alice);
        var (alicePrivateChatId, _) = await Tester.CreateChat(isPublicChat: false, title: "AlicePrivate");
        var (publicChatId, _) = await Tester.CreateChat(isPublicChat: true, title: "Public");

        await Tester.SignIn(bob);
        await Tester.JoinChat(publicChatId, Symbol.Empty);

        await Tester.SignIn(alice);
        var peerChatId = PeerChatId.New(alice.Id, bob.Id);
        await Tester.CreateTextEntry(peerChatId, "hi bob, from alice");

        await using var aliceMcp = await CreateClientWithRawKey(aliceKey);
        await using var bobMcp = await CreateClientWithRawKey(bobKey);

        await AssertCanPost(aliceMcp, alicePrivateChatId);
        await AssertCannotPost(bobMcp, alicePrivateChatId);

        await AssertCanPost(bobMcp, bobPrivateChatId);
        await AssertCannotPost(aliceMcp, bobPrivateChatId);

        await AssertCanPost(aliceMcp, publicChatId);
        await AssertCanPost(bobMcp, publicChatId);
        await AssertCanRead(aliceMcp, publicChatId);
        await AssertCanRead(bobMcp, publicChatId);

        await AssertCanPost(aliceMcp, peerChatId);
        await AssertCanPost(bobMcp, peerChatId);
    }

    [Fact]
    public async Task OutsiderShouldNotManageAPrivateChat()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        var bobKey = await IssueApiKey("bob");
        await Tester.SignIn(alice);
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: "AliceOnly");
        var entry = await Tester.CreateTextEntry(chatId, "mine");
        await using var bobMcp = await CreateClientWithRawKey(bobKey);

        // act
        var updateError = await CallToolExpectingError(bobMcp, "update_chat",
            new { chatId = chatId.Value, title = "Hijacked" });
        var addError = await CallToolExpectingError(bobMcp, "add_members",
            new { chatId = chatId.Value, userIds = new[] { bob.Id.Value } });
        var pinError = await CallToolExpectingError(bobMcp, "pin_message",
            new { chatId = chatId.Value, entryId = entry.LocalId });
        var inviteError = await CallToolExpectingError(bobMcp, "create_invite_link",
            new { chatId = chatId.Value });
        var members = await CallTool<McpListMembersResult>(bobMcp, "list_members", new { chatId = chatId.Value });

        // assert
        updateError.Should().NotBeEmpty();
        addError.Should().NotBeEmpty();
        pinError.Should().NotBeEmpty();
        inviteError.Should().NotBeEmpty();
        members.Members.Should().BeEmpty("members are hidden without the SeeMembers permission");
        var chat = await Tester.Chats.Get(Tester.Session, chatId, CancellationToken.None);
        chat!.Title.Should().Be("AliceOnly");
    }

    private static Task AssertCanPost(McpClient mcp, ChatId chatId)
        => CallTool(mcp, "post_message", new { chatId = chatId.Value, text = $"post into {chatId.Value}" });

    private static async Task AssertCannotPost(McpClient mcp, ChatId chatId)
    {
        var result = await mcp.CallToolAsync("post_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["text"] = $"sneaky post into {chatId.Value}",
        });
        result.IsError.Should().Be(true, $"post_message into {chatId} should be denied");
    }

    private static Task AssertCanRead(McpClient mcp, ChatId chatId)
        => CallTool(mcp, "get_id_range", new { chatId = chatId.Value });
}
