using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpNotificationToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task MentionShouldBeListedWithItsMessage()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Active notifications");
        var aliceAuthor = await Tester.InviteToChat(chatId, alice.Id);
        await using var client = await CreateClientWithRawKey(aliceKey);

        // act
        var entry = await Tester.CreateTextEntry(chatId, $"hi @a:{aliceAuthor.Id}");

        // assert
        McpListNotificationsResult active = null!;
        await TestWait.WhenPolled(async () => {
            active = await CallTool<McpListNotificationsResult>(client, "list_notifications");
            active.Items.Should().Contain(x => x.Kind == "mention");
        }, WaitTimeout);
        var item = active.Items.Single(x => x.Kind == "mention");
        item.ChatId.Should().Be(chatId.Value);
        item.EntryId.Should().Be(entry.LocalId);
        item.AuthorId.Should().Be(entry.AuthorId.Value);
        item.Message.Should().NotBeNull("the anchored message is re-resolved through the caller's session");
        item.Message!.Text.Should().Contain("hi");
        active.Version.Should().NotBeNullOrEmpty();

        var (otherChatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Another chat");
        var otherChat = await CallTool<McpListNotificationsResult>(client, "list_notifications",
            new { chatId = otherChatId.Value });
        otherChat.Items.Should().BeEmpty("the chat filter excludes the mention");
    }

    [Fact]
    public async Task WaitShouldReturnWhenANotificationArrives()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Wait for notifications");
        var aliceAuthor = await Tester.InviteToChat(chatId, alice.Id);
        await using var client = await CreateClientWithRawKey(aliceKey);
        var before = await CallTool<McpListNotificationsResult>(client, "list_notifications");

        // act
        var waitTask = CallTool<McpListNotificationsResult>(client, "wait_for_notifications",
            new { version = before.Version, timeoutSeconds = 30 });
        await Tester.CreateTextEntry(chatId, $"ping @a:{aliceAuthor.Id}");
        var after = await waitTask.WaitAsync(WaitTimeout);

        // assert
        after.Version.Should().NotBe(before.Version);
        after.Items.Should().Contain(x => x.Kind == "mention");
    }

    [Fact]
    public async Task WaitShouldReturnTheSameVersionOnTimeout()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();
        var before = await CallTool<McpListNotificationsResult>(client, "list_notifications");

        // act
        var after = await CallTool<McpListNotificationsResult>(client, "wait_for_notifications",
            new { version = before.Version, timeoutSeconds = 1 });

        // assert
        after.Version.Should().Be(before.Version);
    }

    [Fact]
    public async Task DismissShouldRemoveNotifications()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Dismiss notifications");
        var aliceAuthor = await Tester.InviteToChat(chatId, alice.Id);
        await using var client = await CreateClientWithRawKey(aliceKey);
        await Tester.CreateTextEntry(chatId, $"one @a:{aliceAuthor.Id}");
        await Tester.CreateTextEntry(chatId, $"two @a:{aliceAuthor.Id}");
        McpListNotificationsResult active = null!;
        await TestWait.WhenPolled(async () => {
            active = await CallTool<McpListNotificationsResult>(client, "list_notifications");
            active.Items.Count(x => x.Kind == "mention").Should().Be(2);
        }, WaitTimeout);

        // act
        await CallTool(client, "dismiss_notification", new { id = active.Items.First(x => x.Kind == "mention").Id });

        // assert
        await TestWait.WhenPolled(async () => {
            var afterOne = await CallTool<McpListNotificationsResult>(client, "list_notifications");
            afterOne.Items.Count(x => x.Kind == "mention").Should().Be(1);
        }, WaitTimeout);

        // act
        await CallTool(client, "dismiss_all_notifications");

        // assert
        await TestWait.WhenPolled(async () => {
            var afterAll = await CallTool<McpListNotificationsResult>(client, "list_notifications");
            afterAll.Items.Should().BeEmpty();
        }, WaitTimeout);
    }

    [Fact]
    public async Task InvalidChatIdShouldBeAnError()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();

        // act
        var error = await CallToolExpectingError(client, "list_notifications", new { chatId = "!!!" });

        // assert
        error.Should().Contain("chatId");
    }
}
