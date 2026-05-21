using ActualChat.Notifications.Flows;
using ActualChat.Queues;
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    [Fact]
    public async Task ShouldSendNotificationForOnlineUserWhenUnread()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Test chat");
        await Tester.InviteToChat(chatId, alice);

        await Tester.ForceOffline(alice);

        // Bob sends a message
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello Alice!");

        // act - schedule NotificationFlow with zero delay
        var flowArgs = NotificationFlow.GetArguments(alice.Id, chatId);
        await FlowHub.NewResumeEvent<NotificationFlow>(flowArgs)
            .WithDelay(TimeSpan.Zero)
            .WithDelayQuanta(TimeSpan.Zero)
            .Schedule();
        await Queues.WhenProcessing();

        // assert - notification should appear for Alice
        var notification = await GetNotification(alice, entry.Id);
        notification.Title.Should().Contain("Test chat");
        notification.Text.Should().Be("Hello Alice!");
        notification.EntryId.Should().Be(entry.Id);
    }

    [Fact]
    public async Task ShouldNotSendNotificationForOnlineUserWhenRead()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Test chat 2");
        await Tester.InviteToChat(chatId, alice);

        // Make Alice online
        await Commander.Call(new UserPresencesBackend_CheckIn(alice.Id, Clocks.SystemClock.Now, true));

        // Bob sends a message
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello Alice!");

        // Alice reads the message before the flow fires
        await Commander.Call(new ChatPositionsBackend_Set(
            alice.Id, chatId, ChatPositionKind.Read, new ChatPosition(entry.LocalId)));

        // act - schedule NotificationFlow with zero delay
        var flowArgs = NotificationFlow.GetArguments(alice.Id, chatId);
        await FlowHub.NewResumeEvent<NotificationFlow>(flowArgs)
            .WithDelay(TimeSpan.Zero)
            .WithDelayQuanta(TimeSpan.Zero)
            .Schedule();
        await Queues.WhenProcessing();
        var since = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
        var ids = await Tester.NotificationsBackend.ListRecentNotificationIds(
            alice.Id, since, CancellationToken.None);
        var notifications = await ids
            .Select(x => Tester.NotificationsBackend.Get(x, CancellationToken.None))
            .Collect();
        var matching = notifications
            .SkipNullItems()
            .OfType<ChatEntryRelatedNotification>()
            .Where(x => x.EntryId == entry.Id)
            .ToList();
        matching.Should().BeEmpty("Alice already read the message, so no notification should be sent");
    }

    [Fact]
    public async Task ShouldSendNotificationForFirstUnreadWhenMultipleMessages()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Multi-msg chat");
        await Tester.InviteToChat(chatId, alice);

        await Tester.ForceOffline(alice);

        // Bob sends 3 messages
        await Tester.SignIn(bob);
        var entry1 = await Tester.CreateTextEntry(chatId, "First");
        await Queues.WhenProcessing();
        var entry2 = await Tester.CreateTextEntry(chatId, "Second");
        var entry3 = await Tester.CreateTextEntry(chatId, "Third");

        // act - schedule NotificationFlow once with (userId, chatId) args
        var flowArgs = NotificationFlow.GetArguments(alice.Id, chatId);
        await FlowHub.NewResumeEvent<NotificationFlow>(flowArgs)
            .WithDelay(TimeSpan.Zero)
            .WithDelayQuanta(TimeSpan.Zero)
            .Schedule();
        await Queues.WhenProcessing();

        // assert - exactly 1 notification, for the first unread entry
        var notification = await GetNotification(alice, entry1.Id);
        notification.Title.Should().Contain("Multi-msg chat");
        notification.Text.Should().Be("First");
        notification.EntryId.Should().Be(entry1.Id);
    }

    [Fact]
    public async Task ShouldDeduplicateFlowForSameChatAndUser()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Dedup chat");
        await Tester.InviteToChat(chatId, alice);

        await Tester.ForceOffline(alice);

        // Bob sends a message
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Dedup test message");

        // act - schedule the same flow twice with nonzero quanta (enables UUID-based dedup)
        var flowArgs = NotificationFlow.GetArguments(alice.Id, chatId);
        await FlowHub.NewResumeEvent<NotificationFlow>(flowArgs)
            .WithDelay(TimeSpan.FromMilliseconds(100))
            .WithDelayQuanta(TimeSpan.FromSeconds(1))
            .Schedule();
        await FlowHub.NewResumeEvent<NotificationFlow>(flowArgs)
            .WithDelay(TimeSpan.FromMilliseconds(100))
            .WithDelayQuanta(TimeSpan.FromSeconds(1))
            .Schedule();
        await Queues.WhenProcessing();

        // assert - only 1 notification (not 2)
        var notification = await GetNotification(alice, entry.Id);
        notification.Text.Should().Be("Dedup test message");

        // Verify no duplicate by checking total count for this entry
        await Task.Delay(TimeSpan.FromSeconds(3));
        var since = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
        var ids = await Tester.NotificationsBackend.ListRecentNotificationIds(
            alice.Id, since, CancellationToken.None);
        var notifications = await ids
            .Select(x => Tester.NotificationsBackend.Get(x, CancellationToken.None))
            .Collect();
        var matching = notifications
            .SkipNullItems()
            .OfType<ChatEntryRelatedNotification>()
            .Where(x => x.EntryId == entry.Id)
            .ToList();
        matching.Should().HaveCount(1, "duplicate flow scheduling should not produce duplicate notifications");
    }

    [Fact]
    public async Task ShouldSkipNotificationWhenEntryFreshAndUserOnline()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Fresh skip chat");
        await Tester.InviteToChat(chatId, alice);

        // Make Alice online
        await Commander.Call(new UserPresencesBackend_CheckIn(alice.Id, Clocks.SystemClock.Now, true));

        // Bob sends a message
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Fresh message");

        // act - schedule NotificationFlow with zero delay (entry is < 30s old)
        var flowArgs = NotificationFlow.GetArguments(alice.Id, chatId);
        await FlowHub.NewResumeEvent<NotificationFlow>(flowArgs)
            .WithDelay(TimeSpan.Zero)
            .WithDelayQuanta(TimeSpan.Zero)
            .Schedule();
        await Queues.WhenProcessing();
        var since = Clocks.SystemClock.Now - TimeSpan.FromMinutes(1);
        var ids = await Tester.NotificationsBackend.ListRecentNotificationIds(
            alice.Id, since, CancellationToken.None);
        var notifications = await ids
            .Select(x => Tester.NotificationsBackend.Get(x, CancellationToken.None))
            .Collect();
        var matching = notifications
            .SkipNullItems()
            .OfType<ChatEntryRelatedNotification>()
            .Where(x => x.EntryId == entry.Id)
            .ToList();
        matching.Should().BeEmpty("entry is fresh and user is online, notification should be skipped");
    }

    private async Task<ChatEntryRelatedNotification> GetNotification(AccountFull user, ChatEntryId entryId)
    {
        ChatEntryRelatedNotification? notification = null!;
        await TestExt.When(async () => {
            var ids = await Tester.NotificationsBackend.ListRecentNotificationIds(
                user.Id, Clocks.SystemClock.Now - TimeSpan.FromMinutes(1), CancellationToken.None);
            ids.Should().NotBeEmpty();
            var retrieved = await ids
                .Select(x => Tester.NotificationsBackend.Get(x, CancellationToken.None))
                .Collect();
            var notifications = retrieved.SkipNullItems().OfType<ChatEntryRelatedNotification>().Where(x => x.EntryId == entryId).ToList();
            notifications.Should().HaveCount(1);
            notification = notifications.FirstOrDefault()!;
        }, TimeSpan.FromSeconds(30));
        return notification;
    }
}
