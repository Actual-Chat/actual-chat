using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationDeliveryTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();
    private INotifications Notifications => Tester.AppServices.GetRequiredService<INotifications>();

    [Fact]
    public async Task ListActiveReturnsActiveNotifications()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "ListActive chat");
        await Tester.InviteToChat(chatId, alice);

        await Tester.SignIn(bob);
        await Tester.CreateTextEntry(chatId, "Hi Alice");

        await Tester.SignIn(alice);
        await TestExt.When(async () => {
            var active = await Notifications.ListActive(Tester.Session, CancellationToken.None);
            var notification = active.Should().ContainSingle().Subject;
            notification.Should().BeOfType<MessageNotification>();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task HandleDismissesNotificationAndPushesSilentDismissal()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Dismissal chat");
        await Tester.InviteToChat(chatId, alice);
        var deviceId = await RegisterDevice(alice.Id);
        Sink.Clear();

        await Tester.SignIn(bob);
        await Tester.CreateTextEntry(chatId, "Hi Alice");

        // The notification is created and a delivery push is enqueued (through NATS) + sent.
        Notification notification = null!;
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            notification = info.Displayed.Should().ContainSingle().Subject;
        }, TimeSpan.FromSeconds(10));
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m => !m.IsDismissal && m.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));

        // Alice handles the notification -> it's dropped and a silent dismissal push goes out.
        await Commander.Call(new NotificationsBackend_Handle(notification.Id));

        await TestExt.When(() => {
            var dismissals = Sink.Messages
                .Where(m => m.IsDismissal && m.DeviceIds.Contains(deviceId))
                .ToList();
            dismissals.Should().NotBeEmpty();
            var last = dismissals[^1];
            last.DismissedIds.Should().Contain(notification.Id);
            last.BadgeCount.Should().Be(0);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));

        var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
        info.Displayed.Should().BeEmpty();
    }

    [Fact]
    public async Task MutedChatIsExcludedFromBadgeCount()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chat1, _) = await Tester.CreateChat(false, "Mute chat 1");
        var (chat2, _) = await Tester.CreateChat(false, "Mute chat 2");
        await Tester.InviteToChat(chat1, alice);
        await Tester.InviteToChat(chat2, alice);
        var deviceId = await RegisterDevice(alice.Id);

        // chat1 notifies Alice while unmuted.
        await Tester.SignIn(bob);
        await Tester.CreateTextEntry(chat1, "First in chat1");
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));

        // Alice mutes chat1 *after* it's already displayed.
        await Tester.SignIn(alice);
        await Tester.AppServices.UserSettingsUI(Tester.Session).ChatUserSettings(chat1)
            .Update(x => x with { NotificationMode = ChatNotificationMode.Muted }, CancellationToken.None);

        Sink.Clear();
        await Tester.SignIn(bob);
        await Tester.CreateTextEntry(chat2, "First in chat2");

        // The muted chat1 is excluded from the active set (single source of truth), so only
        // chat2 remains displayed...
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            var displayed = info.Displayed.Should().ContainSingle().Subject;
            displayed.Text.Should().Be("Bobby: First in chat2");
        }, TimeSpan.FromSeconds(10));

        // ...and the chat2 delivery push carries a badge of 1 (chat1 is muted, so excluded).
        await TestExt.When(() => {
            var chat2Push = Sink.Messages
                .Where(m => !m.IsDismissal && m.DeviceIds.Contains(deviceId) && m.Notification!.Text == "Bobby: First in chat2")
                .ToList();
            chat2Push.Should().NotBeEmpty();
            chat2Push[^1].BadgeCount.Should().Be(1);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReadingAChatEagerlyDismissesItsNotification()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Eager dismiss chat");
        await Tester.InviteToChat(chatId, alice);
        var deviceId = await RegisterDevice(alice.Id);
        Sink.Clear();

        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hi Alice");

        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m => !m.IsDismissal && m.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));

        // Alice reads the chat — and crucially NO further message is sent.
        await Commander.Call(new ChatPositionsBackend_Set(
            alice.Id, chatId, ChatPositionKind.Read, new ChatPosition(entry.LocalId)));

        // The read alone triggers reconciliation: a silent dismissal push goes out and the
        // notification leaves the displayed set. The read-reconcile event is delay-collapsed
        // (Constants.Notification.ReadReconcileWindow), so allow for that window.
        await TestExt.When(() => {
            var dismissals = Sink.Messages
                .Where(m => m.IsDismissal && m.DeviceIds.Contains(deviceId))
                .ToList();
            dismissals.Should().NotBeEmpty();
            dismissals[^1].BadgeCount.Should().Be(0);
            return Task.CompletedTask;
        }, Constants.Notification.ReadReconcileWindow + TimeSpan.FromSeconds(10));

        var info2 = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
        info2.Displayed.Should().BeEmpty();
    }

    [Fact]
    public async Task NotificationAnchorsAtFirstUnreadEntryAndAlertsAudibly()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Anchor chat");
        await Tester.InviteToChat(chatId, alice);
        var deviceId = await RegisterDevice(alice.Id);
        Sink.Clear();

        await Tester.SignIn(bob);
        var first = await Tester.CreateTextEntry(chatId, "First unread");

        // The notification is anchored at (and deep-links to) the first unread entry.
        // Multi-message coalescing/aggregation is covered deterministically by the unit tests;
        // here a single message keeps the anchor + link assertions free of coalescing timing.
        MessageNotification notification = null!;
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            notification = info.Displayed.Should().ContainSingle().Subject
                .Should().BeOfType<MessageNotification>().Subject;
        }, TimeSpan.FromSeconds(10));

        notification.StartEntryLid.Should().Be(first.LocalId);
        notification.GetChatLink().Value.Should().Contain($"n={first.LocalId}");

        // The first message alerts audibly (later coalesced updates back off to silent).
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m => !m.IsDismissal && !m.IsSilent && m.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MentionIsDeliveredAudiblyAndCanBeReAlerted()
    {
        var alice = await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false, "Mention chat");
        var deviceId = await RegisterDevice(alice.Id);
        Sink.Clear();

        var entryId = ChatEntryId.New(chatId, 7);
        var authorId = AuthorId.New(chatId, 1);
        var mention = MentionNotification.New(alice.Id, entryId, authorId) with {
            Title = "Bob @ Mention chat",
            Text = "@alice ping",
            SentAt = Clocks.SystemClock.Now,
        };

        // The mention is displayed and delivered audibly (mentions never coalesce into silence).
        await Commander.Call(new NotificationsBackend_Notify(mention));
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Should().ContainSingle(n => n.Id == mention.Id);
        }, TimeSpan.FromSeconds(10));
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m =>
                !m.IsDismissal && !m.IsSilent && m.Notification!.Id == mention.Id && m.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));

        // The reminder flow re-alerts by re-pushing the still-unread mention audibly.
        Sink.Clear();
        await Commander.Call(new NotificationsBackend_Push(mention));
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m =>
                !m.IsDismissal && !m.IsSilent && m.Notification!.Id == mention.Id && m.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task PartialReadReAnchorsNotification()
    {
        var alice = await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false, "Reanchor chat");
        var deviceId = await RegisterDevice(alice.Id);
        var authorId = AuthorId.New(chatId, 1);

        // A coalesced notification spanning entries 5..10.
        var notification = MessageNotification.New(alice.Id, chatId, 10, authorId) with {
            Title = "Bob @ Reanchor chat",
            Text = "early message",
            StartEntryLid = 5,
            UnreadCount = 6,
            AuthorIds = new[] { authorId }.ToApiArray(),
            LeadText = "early message",
            SentAt = Clocks.SystemClock.Now,
        };
        await Commander.Call(new NotificationsBackend_Notify(notification));
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            var n = info.Displayed.Should().ContainSingle().Subject.Should().BeOfType<MessageNotification>().Subject;
            n.StartEntryLid.Should().Be(5);
        }, TimeSpan.FromSeconds(10));

        Sink.Clear();
        // Alice reads through entry 7 (partial: 5 <= 7 < 10) -> the anchor re-points to entry 8.
        await Commander.Call(new ChatPositionsBackend_Set(
            alice.Id, chatId, ChatPositionKind.Read, new ChatPosition(7)));

        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            var n = info.Displayed.Should().ContainSingle().Subject.Should().BeOfType<MessageNotification>().Subject;
            n.StartEntryLid.Should().Be(8);
            n.UnreadCount.Should().Be(3);
        }, Constants.Notification.ReadReconcileWindow + TimeSpan.FromSeconds(10));

        // The re-anchor refreshes the banner silently (a reduction, not a new alert).
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m => !m.IsDismissal && m.IsSilent && m.Notification!.Id == notification.Id);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task HandleAllDismissesEveryNotification()
    {
        var alice = await Tester.SignInAsAlice();
        var (chat1, _) = await Tester.CreateChat(false, "HandleAll 1");
        var (chat2, _) = await Tester.CreateChat(false, "HandleAll 2");
        var deviceId = await RegisterDevice(alice.Id);

        var n1 = NewFeedNotification(alice.Id, chat1, 3, "hi 1");
        var n2 = NewFeedNotification(alice.Id, chat2, 4, "hi 2");
        await Commander.Call(new NotificationsBackend_Notify(n1));
        await Commander.Call(new NotificationsBackend_Notify(n2));
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Count.Should().Be(2);
        }, TimeSpan.FromSeconds(10));

        Sink.Clear();
        // "Mark all read" clears the whole feed in one round-trip.
        await Commander.Call(new Notifications_HandleAll(Tester.Session));

        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Should().BeEmpty();
        }, TimeSpan.FromSeconds(10));
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m => m.IsDismissal && m.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    private static MessageNotification NewFeedNotification(UserId userId, ChatId chatId, long entryLid, string text)
    {
        var authorId = AuthorId.New(chatId, 1);
        return MessageNotification.New(userId, chatId, entryLid, authorId) with {
            Title = $"Bob @ {chatId.Value}",
            Text = text,
            StartEntryLid = entryLid,
            UnreadCount = 1,
            AuthorIds = new[] { authorId }.ToApiArray(),
            LeadText = text,
            LeadCount = 1,
            SentAt = Moment.Now,
        };
    }

    private async Task<Symbol> RegisterDevice(UserId userId)
    {
        var deviceId = new Symbol("test-device-" + userId.Value);
        await Commander.Call(new NotificationsBackend_RegisterDevice(userId, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        return deviceId;
    }
}
