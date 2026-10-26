using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationRemovedEntryTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    private FirebaseMessagingTestSink Sink
        => field ??= AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();

    private IChatsBackend ChatsBackend => field ??= AppHost.Services.GetRequiredService<IChatsBackend>();

    [Fact]
    public async Task MessageShouldBeDroppedWhenTheEntriesLeftUnreadAreRemoved()
    {
        // arrange
        var (alice, chatId) = await SetUpAliceAndBobChat("Removed unread tail");
        var first = await Tester.CreateTextEntry(chatId, "First");
        var last = await Tester.CreateTextEntry(chatId, "Last");
        await SetReadPosition(alice.Id, chatId, first.LocalId);
        await WhenNotified(alice.Id, n => n is MessageNotification);

        // act
        await Tester.RemoveTextEntry(last.Id);

        // assert
        await TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().NotContain(n => n is MessageNotification,
                "the reader has seen every entry left in the chat, and the removed one can't come on screen");
        }, WaitTimeout);
    }

    [Fact]
    public async Task MessageShouldBeDroppedWhenAllItsEntriesAreRemoved()
    {
        // arrange
        var (alice, chatId) = await SetUpAliceAndBobChat("Removed whole window");
        var entry = await Tester.CreateTextEntry(chatId, "Only one");
        await WhenNotified(alice.Id, n => n is MessageNotification);

        // act
        await Tester.RemoveTextEntry(entry.Id);

        // assert
        await TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().NotContain(n => n is MessageNotification,
                "nothing the notification covers is left, read or not");
        }, WaitTimeout);
    }

    [Fact]
    public async Task MessageShouldSurviveWhileAnEntryItCoversIsUnread()
    {
        // arrange
        var (alice, chatId) = await SetUpAliceAndBobChat("Removed with unread survivor");
        await Tester.CreateTextEntry(chatId, "First");
        await WhenNotified(alice.Id, n => n is MessageNotification);
        var last = await Tester.CreateTextEntry(chatId, "Last");
        await WhenNotified(alice.Id, n => n is MessageNotification { EntryLid: var lid } && lid == last.LocalId);

        // act
        await Tester.RemoveTextEntry(last.Id);
        await TestWait.WhenPolled(async () => {
            var removed = await ChatsBackend.GetEntry(last.Id, CancellationToken.None);
            removed.Should().BeNull();
        }, WaitTimeout);

        // assert
        var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
        info.Items.Should().Contain(n => n is MessageNotification,
            "the first entry is still there and still unread");
    }

    [Fact]
    public async Task MentionShouldBeDroppedWhenItsEntryIsRemoved()
    {
        // arrange
        var (alice, chatId) = await SetUpAliceAndBobChat("Removed mention");
        var mention = await Tester.CreateTextEntry(chatId, $"ping @u:{alice.Id} !");
        await Tester.CreateTextEntry(chatId, "Something else");
        await WhenNotified(alice.Id, n => n is MentionNotification);

        // act
        await Tester.RemoveTextEntry(mention.Id);

        // assert
        await TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().NotContain(n => n is MentionNotification,
                "a mention goes with its entry, even while the chat stays unread");
        }, WaitTimeout);
    }

    [Fact]
    public async Task RemovalShouldSendTheDismissalWithoutAnyoneReadingTheActiveSet()
    {
        // arrange
        var (alice, chatId) = await SetUpAliceAndBobChat("Removed while offline");
        var deviceId = new Symbol("removed-entry-device-" + alice.Id.Value);
        var registerDeviceCmd = new NotificationsBackend_RegisterDevice(
            alice.Id, deviceId, DeviceType.WebBrowser, Symbol.Empty);
        await Commander.Call(registerDeviceCmd);
        var entry = await Tester.CreateTextEntry(chatId, "Taken back");
        var notification = await WhenNotified(alice.Id, n => n is MessageNotification);

        // act
        await Tester.RemoveTextEntry(entry.Id);

        // assert
        // Only the sink is polled: a read of the active set would trigger the cleanup by itself.
        await TestWait.WhenPolled(() => {
            var isDismissed = Sink.Messages
                .Any(m => m.DismissedIds.Contains(notification.Id) && m.DeviceIds.Contains(deviceId));
            isDismissed.Should().BeTrue(
                "removing a just-posted entry must reach the devices of a member who reads nothing");
        }, WaitTimeout);
    }

    // Private methods

    private async Task<(AccountFull Alice, ChatId ChatId)> SetUpAliceAndBobChat(string chatTitle)
    {
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false, chatTitle);
        await Tester.InviteToChat(chatId, bob);
        await Tester.SignIn(bob);
        return (alice, chatId);
    }

    private async Task<Notification> WhenNotified(UserId userId, Func<Notification, bool> predicate)
    {
        Notification notification = null!;
        await TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(userId, CancellationToken.None);
            notification = info.Items.Should().Contain(n => predicate(n)).Which;
        }, WaitTimeout);
        return notification;
    }

    private Task SetReadPosition(UserId userId, ChatId chatId, long entryLid)
    {
        var setPositionCmd = new ChatPositionsBackend_Set(
            userId, chatId, ChatPositionKind.Read, new ChatPosition(entryLid));
        return Commander.Call(setPositionCmd);
    }
}
