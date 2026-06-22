using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualChat.Users;

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

        // Both chats are displayed (mute doesn't retroactively remove chat1)...
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Should().HaveCount(2);
        }, TimeSpan.FromSeconds(10));

        // ...but the chat2 delivery push carries a badge of 1 (chat1 is muted, so excluded).
        await TestExt.When(() => {
            var chat2Push = Sink.Messages
                .Where(m => !m.IsDismissal && m.DeviceIds.Contains(deviceId) && m.Notification!.Text == "First in chat2")
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

    private async Task<Symbol> RegisterDevice(UserId userId)
    {
        var deviceId = new Symbol("test-device-" + userId.Value);
        await Commander.Call(new NotificationsBackend_RegisterDevice(userId, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        return deviceId;
    }
}
