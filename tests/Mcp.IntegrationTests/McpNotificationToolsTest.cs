using ActualChat.Notifications;
using ActualChat.Queues;
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
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Notification history");
        var aliceAuthor = await Tester.InviteToChat(chatId, alice.Id);
        await using var client = await CreateClientWithRawKey(aliceKey);

        // act
        var entry = await Tester.CreateTextEntry(chatId, $"hi @a:{aliceAuthor.Id}");

        // assert
        McpListNotificationsResult mentions = null!;
        await TestExt.When(async () => {
            mentions = await CallTool<McpListNotificationsResult>(client, "list_notifications",
                new { kinds = new[] { "mention" } });
            mentions.Items.Should().ContainSingle();
        }, WaitTimeout);
        var item = mentions.Items.Single();
        item.Kind.Should().Be("mention");
        item.ChatId.Should().Be(chatId.Value);
        item.EntryId.Should().Be(entry.LocalId);
        item.AuthorId.Should().Be(entry.AuthorId.Value);
        item.Message.Should().NotBeNull("the anchored message is re-resolved through the caller's session");
        item.Message!.Text.Should().Contain("hi");
        mentions.NextAfterSeq.Should().Be(item.Seq);

        var reactions = await CallTool<McpListNotificationsResult>(client, "list_notifications",
            new { kinds = new[] { "reaction" } });
        reactions.Items.Should().BeEmpty("the kind filter excludes the mention");
        reactions.NextAfterSeq.Should().BeNull();

        var nothingNew = await CallTool<McpListNotificationsResult>(client, "list_notifications",
            new { afterSeq = mentions.NextAfterSeq });
        nothingNew.Items.Should().BeEmpty("the cursor skips what the caller has already seen");
    }

    [Fact]
    public async Task UnknownKindShouldBeAnError()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();

        // act
        var error = await CallToolExpectingError(client, "list_notifications", new { kinds = new[] { "bogus" } });

        // assert
        error.Should().Contain("bogus");
    }

    [Fact]
    public async Task MessageWithoutAccessShouldBeNull()
    {
        // arrange - a mention anchored at a private chat the caller is not a member of yields no message body
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Not Alice's chat");
        var entry = await Tester.CreateTextEntry(chatId, "private");
        var mention = MentionNotification.New(alice.Id, entry.Id, entry.AuthorId) with {
            SentAt = Clocks.SystemClock.Now, Title = "Chat", Text = "@you",
        };
        await Queues.Enqueue(new UserNotifiedEvent(mention));
        await using var client = await CreateClientWithRawKey(aliceKey);

        // act & assert
        await TestExt.When(async () => {
            var result = await CallTool<McpListNotificationsResult>(client, "list_notifications", new { });
            var item = result.Items.Should().ContainSingle().Subject;
            item.Text.Should().Be("@you");
            item.Message.Should().BeNull("the caller cannot read the chat the row anchors at");
        }, WaitTimeout);
    }
}
