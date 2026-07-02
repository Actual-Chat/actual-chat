using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationModeTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();

    [Theory]
    [InlineData(NotificationKind.Message, ChatNotificationMode.Default, true)]
    [InlineData(NotificationKind.Message, ChatNotificationMode.ImportantOnly, false)]
    [InlineData(NotificationKind.Message, ChatNotificationMode.Muted, false)]
    [InlineData(NotificationKind.Thread, ChatNotificationMode.ImportantOnly, false)]
    [InlineData(NotificationKind.Conversation, ChatNotificationMode.Muted, false)]
    [InlineData(NotificationKind.Reaction, ChatNotificationMode.Default, true)]
    [InlineData(NotificationKind.Reaction, ChatNotificationMode.ImportantOnly, false)]
    [InlineData(NotificationKind.Reaction, ChatNotificationMode.Muted, false)]
    [InlineData(NotificationKind.Mention, ChatNotificationMode.Default, true)]
    [InlineData(NotificationKind.Mention, ChatNotificationMode.ImportantOnly, true)]
    [InlineData(NotificationKind.Mention, ChatNotificationMode.Muted, false)]
    [InlineData(NotificationKind.Reply, ChatNotificationMode.ImportantOnly, true)]
    [InlineData(NotificationKind.Attention, ChatNotificationMode.Default, true)]
    [InlineData(NotificationKind.Attention, ChatNotificationMode.ImportantOnly, true)]
    [InlineData(NotificationKind.Attention, ChatNotificationMode.Muted, true)]
    [InlineData(NotificationKind.IncomingCall, ChatNotificationMode.ImportantOnly, true)]
    [InlineData(NotificationKind.IncomingCall, ChatNotificationMode.Muted, true)]
    public void DeliverabilityMatrixIsHonored(NotificationKind kind, ChatNotificationMode mode, bool expected)
        => NotificationHelper.IsDeliverable(NotificationHelper.GetImportance(kind), mode)
            .Should().Be(expected);

    [Fact]
    public async Task ImportantOnlyDeliversInTextMentionButNotMessage()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "ImportantOnly chat");
        await Tester.InviteToChat(chatId, alice);
        var deviceId = await RegisterDevice(alice.Id);

        await Tester.SignIn(alice);
        await SetNotificationMode(chatId, ChatNotificationMode.ImportantOnly);
        Sink.Clear();

        await Tester.SignIn(bob);
        await Tester.CreateTextEntry(chatId, "plain message");
        await Tester.CreateTextEntry(chatId, $"ping @u:{alice.Id} !");

        // The in-text mention arrives as a personal Mention notification...
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            var notification = info.Displayed.Should().ContainSingle().Subject;
            notification.Kind.Should().Be(NotificationKind.Mention);
        }, TimeSpan.FromSeconds(10));
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m =>
                !m.IsDismissal && m.Notification!.Kind == NotificationKind.Mention && m.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));

        // ...while the plain message was never displayed or pushed.
        Sink.Messages.Should().NotContain(m =>
            !m.IsDismissal && m.Notification!.Kind == NotificationKind.Message && m.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task MutedChatStillDeliversExplicitNotifyActions()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Muted ringer chat");
        await Tester.InviteToChat(chatId, alice);
        var deviceId = await RegisterDevice(alice.Id);

        await Tester.SignIn(alice);
        await SetNotificationMode(chatId, ChatNotificationMode.Muted);
        Sink.Clear();

        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "please look at this");

        // The explicit "notify all" action is a ringer: it breaks through the mute.
        await Commander.Call(new NotificationsBackend_NotifyMembers(bob.Id, chatId, entry.LocalId));
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Should().Contain(n => n.Kind == NotificationKind.Attention);
        }, TimeSpan.FromSeconds(10));
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m =>
                !m.IsDismissal && m.Notification!.Kind == NotificationKind.Attention && m.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));

        // So does the explicit "notify mentioned members" action.
        Sink.Clear();
        await Commander.Call(new NotificationsBackend_NotifyMentionedMembers(bob.Id, entry.Id, [alice.Id]));
        await TestExt.When(() => {
            Sink.Messages.Should().Contain(m =>
                !m.IsDismissal && m.Notification!.Kind == NotificationKind.Attention && m.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));

        // The plain message itself stayed suppressed.
        Sink.Messages.Should().NotContain(m =>
            !m.IsDismissal && m.Notification!.Kind == NotificationKind.Message && m.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task MutedChatSuppressesInTextMention()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (mutedChatId, _) = await Tester.CreateChat(false, "Muted mention chat");
        var (sentinelChatId, _) = await Tester.CreateChat(false, "Sentinel chat");
        await Tester.InviteToChat(mutedChatId, alice);
        await Tester.InviteToChat(sentinelChatId, alice);
        var deviceId = await RegisterDevice(alice.Id);

        await Tester.SignIn(alice);
        await SetNotificationMode(mutedChatId, ChatNotificationMode.Muted);
        Sink.Clear();

        // Mute wins over in-text mentions; the sentinel message in the unmuted chat proves the
        // pipeline drained before the absence assertion.
        await Tester.SignIn(bob);
        await Tester.CreateTextEntry(mutedChatId, $"hey @u:{alice.Id} !");
        await Tester.CreateTextEntry(sentinelChatId, "sentinel");

        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            var notification = info.Displayed.Should().ContainSingle().Subject;
            notification.Text.Should().Be("sentinel");
        }, TimeSpan.FromSeconds(10));
        Sink.Messages.Should().NotContain(m =>
            !m.IsDismissal && m.Notification!.Kind == NotificationKind.Mention && m.DeviceIds.Contains(deviceId));
    }

    [Theory]
    [InlineData(ChatNotificationMode.ImportantOnly)]
    [InlineData(ChatNotificationMode.Muted)]
    public async Task ReactionIsDeliveredOnlyInDefaultMode(ChatNotificationMode suppressingMode)
    {
        var bob = await Tester.SignInAsBob();
        var alice = await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false, $"Reaction {suppressingMode} chat");
        await Tester.InviteToChat(chatId, bob);
        var deviceId = await RegisterDevice(alice.Id);

        var entry1 = await Tester.CreateTextEntry(chatId, "first");
        var entry2 = await Tester.CreateTextEntry(chatId, "second");
        await SetNotificationMode(chatId, suppressingMode);
        Sink.Clear();

        // Suppressed reaction first, then a delivered one after flipping back to Default —
        // the delivered one doubles as the drain sentinel for the absence assertion.
        await Tester.SignIn(bob);
        await Tester.React(entry1.Id, Emojis.Love);

        await Tester.SignIn(alice);
        await SetNotificationMode(chatId, ChatNotificationMode.Default);
        await Tester.SignIn(bob);
        await Tester.React(entry2.Id, Emojis.Love);

        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            var notification = info.Displayed.Should().ContainSingle().Subject
                .Should().BeOfType<ReactionNotification>().Subject;
            notification.EntryLid.Should().Be(entry2.LocalId);
        }, TimeSpan.FromSeconds(10));
        Sink.Messages
            .Where(m => !m.IsDismissal && m.DeviceIds.Contains(deviceId))
            .Select(m => m.Notification)
            .OfType<ReactionNotification>()
            .Should().NotContain(r => r.EntryLid == entry1.LocalId);
    }

    private async Task SetNotificationMode(ChatId chatId, ChatNotificationMode mode)
        => await Tester.AppServices.UserSettingsUI(Tester.Session).ChatUserSettings(chatId)
            .Update(x => x with { NotificationMode = mode }, CancellationToken.None);

    private async Task<Symbol> RegisterDevice(UserId userId)
    {
        var deviceId = new Symbol("test-device-" + userId.Value);
        await Commander.Call(
            new NotificationsBackend_RegisterDevice(userId, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        return deviceId;
    }
}
