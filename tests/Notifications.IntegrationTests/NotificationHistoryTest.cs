using ActualChat.Queues;
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationHistoryTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private INotificationsBackend Backend => Tester.NotificationsBackend;

    [Fact]
    public async Task MentionShouldBeLoggedAndSurviveRead()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false, "Notification history chat");
        var aliceAuthor = await Tester.InviteToChat(chatId, alice.Id);

        // act
        var entry = await Tester.CreateTextEntry(chatId, $"hi @a:{aliceAuthor.Id}");

        // assert
        NotificationHistoryItem item = null!;
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            item = items.Should().ContainSingle(x => x.Kind == NotificationKind.Mention).Subject;
        }, WaitTimeout);
        item.ChatId.Should().Be(chatId);
        item.EntryId.Should().Be(entry.Id);
        item.AuthorId.Should().Be(entry.AuthorId);
        item.Seq.Should().BePositive();

        await Commander.Call(new ChatPositionsBackend_Set(
            alice.Id, chatId, ChatPositionKind.Read, new ChatPosition(entry.LocalId)));
        await TestExt.When(async () => {
            var info = await Backend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().NotContain(n => n.Kind == NotificationKind.Mention,
                "reading the chat clears the active mention");
        }, WaitTimeout);
        var after = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        after.Should().ContainSingle(x => x.Kind == NotificationKind.Mention,
            "the log is not the active set: a read must not erase history");
    }

    [Fact]
    public async Task MessageKindShouldNotBeLogged()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var chatId = ChatId.Parse("the-actual-one");
        var authorId = AuthorId.New(chatId, 1);
        var now = Clocks.SystemClock.Now;
        var message = MessageNotification.New(alice.Id, chatId, 5, authorId) with {
            SentAt = now, Title = "Chat", Text = "plain traffic",
        };
        var mention = MentionNotification.New(alice.Id, ChatEntryId.New(chatId, 6), authorId) with {
            SentAt = now, Title = "Chat", Text = "@you",
        };

        // act
        await Queues.Enqueue(new UserNotifiedEvent(message));
        await Queues.Enqueue(new UserNotifiedEvent(mention));

        // assert
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            items.Should().ContainSingle(x => x.Kind == NotificationKind.Mention);
        }, WaitTimeout);
        var all = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        all.Should().NotContain(x => x.Kind == NotificationKind.Message, "per-chat traffic is not addressed to anyone");
    }

    [Fact]
    public async Task RedeliveredEventShouldNotDuplicateTheRow()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var chatId = ChatId.Parse("the-actual-one");
        var mention = MentionNotification.New(alice.Id, ChatEntryId.New(chatId, 7), AuthorId.New(chatId, 1)) with {
            SentAt = Clocks.SystemClock.Now, Title = "Chat", Text = "@you",
        };

        // act
        await Queues.Enqueue(new UserNotifiedEvent(mention));
        await Queues.Enqueue(new UserNotifiedEvent(mention));

        // assert
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            items.Should().ContainSingle();
        }, WaitTimeout);
        await Task.Delay(500);
        var all = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        all.Should().ContainSingle(
            "the same notification with the same SentAt is one row however often it is delivered");
    }
}
