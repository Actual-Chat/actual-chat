using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationDismissalReliabilityTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();

    [Fact]
    public async Task DismissalShouldClearThePendingEntryOnceItIsSent()
    {
        // arrange
        var (alice, notification, deviceId) = await SetUpNotifiedAlice("Pending dismissal — sent");

        // act
        await Commander.Call(new NotificationsBackend_Dismiss(notification.Id));

        // assert
        await TestExt.When(async () => {
            Sink.Messages.Should().Contain(m => m.IsDismissal && m.DeviceIds.Contains(deviceId));
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.PendingDismissals.Should().BeEmpty("a sent dismissal is no longer owed");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task FailedDismissalShouldStayOwed()
    {
        // arrange
        var (alice, notification, _) = await SetUpNotifiedAlice("Pending dismissal — failed send");
        Sink.FailDismissalCount = int.MaxValue;

        // act
        await Commander.Call(new NotificationsBackend_Dismiss(notification.Id));

        // assert
        // The notification is gone from Items, so nothing but PendingDismissals can re-derive the
        // dismissal - which is the whole reason it is committed alongside the removal.
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().BeEmpty();
            info.PendingDismissals.Should().ContainSingle(
                "a dismissal whose send failed is still owed").Which.Id.Should().Be(notification.Id);
        }, TimeSpan.FromSeconds(10));

        // act 2 — the retry NotificationConvergeFlow performs
        Sink.FailDismissalCount = 0;
        await Commander.Call(new NotificationsBackend_Converge(alice.Id));

        // assert 2
        var afterRetry = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
        afterRetry.PendingDismissals.Should().BeEmpty("the retry sent it");
    }

    [Fact]
    public async Task DismissalRejectedByFcmShouldStayOwed()
    {
        // arrange
        var (alice, notification, _) = await SetUpNotifiedAlice("Pending dismissal — rejected send");
        Sink.RejectDismissalCount = int.MaxValue;

        // act
        await Commander.Call(new NotificationsBackend_Dismiss(notification.Id));

        // assert
        // FCM reports a rejected push in the batch response instead of throwing, so the send
        // completes normally. Waiting on the observed rejection (rather than polling the blob)
        // keeps this off the race with the converge event the dismissal queues.
        await TestExt.When(() => Sink.RejectedDismissals.Should().BeGreaterThan(0),
            TimeSpan.FromSeconds(10));

        // act 2 — the retry NotificationConvergeFlow performs
        Sink.Clear();
        Sink.RejectDismissalCount = 0;
        await Commander.Call(new NotificationsBackend_Converge(alice.Id));

        // assert 2
        // Nothing but PendingDismissals can re-derive this - the notification is gone from Items -
        // so a rejected send that cleared it would leave the banner up for good.
        Sink.Messages.Should().Contain(
            m => m.IsDismissal && m.DismissedIds.Contains(notification.Id),
            "a dismissal FCM rejected must be re-sent, not dropped");
    }

    [Fact]
    public async Task DismissalShouldSendTheBadgeToIosOnItsOwnPush()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Badge push chat");
        await Tester.InviteToChat(chatId, alice);
        var iosDeviceId = new Symbol("test-ios-" + alice.Id.Value);
        var webDeviceId = new Symbol("test-web-" + alice.Id.Value);
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            alice.Id, iosDeviceId, DeviceType.iOSApp, Symbol.Empty));
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            alice.Id, webDeviceId, DeviceType.WebBrowser, Symbol.Empty));
        Sink.Clear();

        await Tester.SignIn(bob);
        await Tester.CreateTextEntry(chatId, "Hi Alice");
        Notification notification = null!;
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            notification = info.Items.Should().ContainSingle().Subject;
        }, TimeSpan.FromSeconds(10));

        // act
        await Commander.Call(new NotificationsBackend_Dismiss(notification.Id));

        // assert
        // The badge rides an alert push so it lands even when the background push carrying the
        // removal is held - and only iOS has one, so nothing else should be woken for it.
        await TestExt.When(() => {
            var badge = Sink.Badges.Should().ContainSingle().Subject;
            badge.DeviceIds.Should().Equal([iosDeviceId]);
            badge.BadgeCount.Should().Be(0);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    // Private methods

    private async Task<(AccountFull Alice, Notification Notification, Symbol DeviceId)> SetUpNotifiedAlice(
        string chatTitle)
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, chatTitle);
        await Tester.InviteToChat(chatId, alice);
        var deviceId = new Symbol("test-device-" + alice.Id.Value);
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            alice.Id, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        Sink.Clear();

        await Tester.SignIn(bob);
        await Tester.CreateTextEntry(chatId, "Hi Alice");

        Notification notification = null!;
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            notification = info.Items.Should().ContainSingle().Subject;
        }, TimeSpan.FromSeconds(10));
        return (alice, notification, deviceId);
    }
}
