using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public sealed class McpChatActivityToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task WaitForChangesShouldReturnTheChatThatGotAMessage()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        await Tester.SignInAsUniqueBob();
        var chatId = await CreateChatWithAlice(alice, "Wait for changes");
        await using var client = await CreateClientWithRawKey(aliceKey);
        var first = await Tester.CreateTextEntry(chatId, "first");
        var before = await TestWait.WhenPolled(async () => {
            var result = await CallTool<McpWaitForChangesResult>(client, "wait_for_changes");
            var unread = await CallTool<McpListUnreadChatsResult>(client, "list_unread_chats");
            unread.Chats.Should().Contain(c => c.ChatId == chatId.Value && c.LastId == first.LocalId,
                "the invited chat must be on the caller's chat list before the wait starts");
            return result;
        }, WaitTimeout);

        // act
        var waitTask = CallTool<McpWaitForChangesResult>(client, "wait_for_changes",
            new { cursor = before.Cursor, timeoutSeconds = 30 });
        var second = await Tester.CreateTextEntry(chatId, "second");
        var after = await waitTask.WaitAsync(WaitTimeout);

        // assert
        before.Changes.Should().BeEmpty("an empty cursor only returns the current one");
        after.Changes.Should().ContainSingle()
            .Which.Should().Be(new McpChatChange(chatId.Value, first.LocalId, second.LocalId));
        after.Cursor.Should().NotBe(before.Cursor);
    }

    [Fact]
    public async Task WaitForChangesShouldReturnTheSameCursorOnTimeout()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Quiet chat");
        await Tester.AppServices.WaitForOpeningEntry(chatId);
        var client = await CreateClient();
        var before = await CallTool<McpWaitForChangesResult>(client, "wait_for_changes");

        // act
        var after = await CallTool<McpWaitForChangesResult>(client, "wait_for_changes",
            new { cursor = before.Cursor, timeoutSeconds = 1 });

        // assert
        after.Changes.Should().BeEmpty();
        after.Cursor.Should().Be(before.Cursor);
    }

    [Fact]
    public async Task MarkReadShouldRemoveTheChatFromUnreadChats()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        await Tester.SignInAsUniqueBob();
        var chatId = await CreateChatWithAlice(alice, "Unread chats");
        await using var client = await CreateClientWithRawKey(aliceKey);
        await Tester.CreateTextEntry(chatId, "one");
        var two = await Tester.CreateTextEntry(chatId, "two");
        var unread = await TestWait.WhenPolled(async () => {
            var result = await CallTool<McpListUnreadChatsResult>(client, "list_unread_chats");
            result.Chats.Should().Contain(c => c.ChatId == chatId.Value && c.LastId == two.LocalId);
            return result.Chats.Single(c => c.ChatId == chatId.Value);
        }, WaitTimeout);

        // act
        await CallTool(client, "mark_read", new { chatId = chatId.Value, lastId = two.LocalId });

        // assert
        unread.ReadId.Should().BeLessThan(two.LocalId);
        unread.UnreadCount.Should().Be(two.LocalId - unread.ReadId);
        await TestWait.WhenPolled(async () => {
            var result = await CallTool<McpListUnreadChatsResult>(client, "list_unread_chats");
            result.Chats.Should().NotContain(c => c.ChatId == chatId.Value);
        }, WaitTimeout);
    }

    [Fact]
    public async Task WaitForUnreadShouldReturnTheChatThatGotAMessage()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        await Tester.SignInAsUniqueBob();
        var chatId = await CreateChatWithAlice(alice, "Wait for unread");
        await using var client = await CreateClientWithRawKey(aliceKey);
        var first = await Tester.CreateTextEntry(chatId, "first");
        await TestWait.WhenPolled(async () => {
            var result = await CallTool<McpListUnreadChatsResult>(client, "list_unread_chats");
            result.Chats.Should().Contain(c => c.ChatId == chatId.Value && c.LastId == first.LocalId);
        }, WaitTimeout);
        await CallTool(client, "mark_read", new { chatId = chatId.Value, lastId = first.LocalId });
        await TestWait.WhenPolled(async () => {
            var result = await CallTool<McpListUnreadChatsResult>(client, "list_unread_chats");
            result.Chats.Should().BeEmpty();
        }, WaitTimeout);

        // act
        var waitTask = CallTool<McpListUnreadChatsResult>(client, "wait_for_unread", new { timeoutSeconds = 30 });
        var second = await Tester.CreateTextEntry(chatId, "second");
        var unread = await waitTask.WaitAsync(WaitTimeout);

        // assert
        unread.Chats.Should().ContainSingle()
            .Which.Should().Be(new McpUnreadChat(chatId.Value, first.LocalId, second.LocalId, 1));
    }

    [Fact]
    public async Task WaitForUnreadShouldReturnNoChatsOnTimeout()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Read chat");
        await Tester.AppServices.WaitForOpeningEntry(chatId);
        var entry = await Tester.CreateTextEntry(chatId, "own message");
        var client = await CreateClient();
        await CallTool(client, "mark_read", new { chatId = chatId.Value, lastId = entry.LocalId });

        // act
        var unread = await CallTool<McpListUnreadChatsResult>(client, "wait_for_unread", new { timeoutSeconds = 1 });

        // assert
        unread.Chats.Should().BeEmpty();
        unread.Cursor.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvalidCursorShouldBeAnError()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();

        // act
        var error = await CallToolExpectingError(client, "wait_for_changes", new { cursor = "garbage" });

        // assert
        error.Should().Contain("cursor");
    }

    // Private methods

    private async Task<ChatId> CreateChatWithAlice(AccountFull alice, string title)
    {
        // Join entries are posted asynchronously, and one landing after a test's own message
        // shifts every lid and cursor this class compares
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: title);
        await Tester.AppServices.WaitForOpeningEntry(chatId);
        var aliceAuthor = await Tester.InviteToChat(chatId, alice.Id);
        await Tester.WaitForJoinEntry(chatId, aliceAuthor.Id);
        return chatId;
    }
}
