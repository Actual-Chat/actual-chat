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

    private static async Task AssertCanPost(McpClient mcp, ChatId chatId)
    {
        var result = await mcp.CallToolAsync("post_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["text"] = $"post into {chatId.Value}",
        });
        result.IsError.Should().NotBe(true, $"post_message into {chatId} should succeed");
    }

    private static async Task AssertCannotPost(McpClient mcp, ChatId chatId)
    {
        var result = await mcp.CallToolAsync("post_message", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
            ["text"] = $"sneaky post into {chatId.Value}",
        });
        result.IsError.Should().Be(true, $"post_message into {chatId} should be denied");
    }

    private static async Task AssertCanRead(McpClient mcp, ChatId chatId)
    {
        var result = await mcp.CallToolAsync("get_id_range", new Dictionary<string, object?> {
            ["chatId"] = chatId.Value,
        });
        result.IsError.Should().NotBe(true, $"get_id_range for {chatId} should succeed");
    }
}
