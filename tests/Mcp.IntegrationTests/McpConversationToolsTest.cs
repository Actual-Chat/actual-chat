using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpConversationToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ListConversationsShouldReturnNewestFirstAndGetShouldResolveOne()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var entries = await Tester.CreateTextEntries(chatId, "msg", 6);
        var first = await Tester.CreateConversation(entries[0], entries[2], "first talk");
        var second = await Tester.CreateConversation(entries[3], entries[5], "second talk");
        var client = await CreateClient();

        // act
        var listed = await WaitFor(
            () => CallTool<McpListConversationsResult>(client, "list_conversations", new { chatId = chatId.Value }),
            r => r.Conversations.Length == 2);
        var fetched = await CallTool<McpConversation>(client, "get_conversation",
            new { conversationId = second.Id.Value });

        // assert
        listed.Conversations.Select(c => c.Id).Should().ContainInOrder(second.Id.Value, first.Id.Value);
        fetched.Summary.Should().Be("second talk");
        fetched.ChatId.Should().Be(chatId.Value);
        fetched.StartEntryId.Should().Be(entries[3].LocalId);
        fetched.EndEntryId.Should().Be(entries[5].LocalId);
        fetched.MessageCount.Should().Be(3);
    }

    [Fact]
    public async Task ListConversationsShouldPageWithBeforeId()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: true);
        var entries = await Tester.CreateTextEntries(chatId, "msg", 4);
        var first = await Tester.CreateConversation(entries[0], entries[1], "one");
        var second = await Tester.CreateConversation(entries[2], entries[3], "two");
        var client = await CreateClient();

        // act
        var page1 = await WaitFor(
            () => CallTool<McpListConversationsResult>(client, "list_conversations",
                new { chatId = chatId.Value, limit = 1 }),
            r => r.Conversations.Length == 1 && r.NextBeforeId != null);
        var page2 = await CallTool<McpListConversationsResult>(client, "list_conversations",
            new { chatId = chatId.Value, limit = 1, beforeId = page1.NextBeforeId });

        // assert
        page1.Conversations.Single().Id.Should().Be(second.Id.Value);
        page2.Conversations.Single().Id.Should().Be(first.Id.Value);
    }
}
