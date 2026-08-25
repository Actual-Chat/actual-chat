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
