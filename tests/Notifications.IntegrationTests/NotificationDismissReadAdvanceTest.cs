using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationDismissReadAdvanceTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IChatPositionsBackend ChatPositionsBackend
        => field ??= Tester.AppServices.GetRequiredService<IChatPositionsBackend>();

    [Fact]
    public async Task DismissAllShouldAdvanceReadPositionOfOnReadNotifications()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Dismiss-all read advance chat");
        await Tester.InviteToChat(chatId, alice);
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello Alice!");
        await WhenNotified(alice.Id);

        // act
        await Tester.SignIn(alice);
        await Tester.Commander.Call(new Notifications_DismissAll { Session = Tester.Session });

        // assert
        await TestExt.When(async () => {
            var position = await GetReadPosition(alice.Id, chatId);
            position.Should().BeGreaterThanOrEqualTo(entry.LocalId,
                "dismissing an OnRead notification must satisfy its mode, not just drop it");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task DismissShouldAdvanceReadPositionOfOneNotification()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Dismiss read advance chat");
        await Tester.InviteToChat(chatId, alice);
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello Alice!");
        var notificationId = (await WhenNotified(alice.Id)).Id;

        // act
        await Tester.SignIn(alice);
        await Tester.Commander.Call(new Notifications_Dismiss {
            Session = Tester.Session,
            NotificationId = notificationId,
        });

        // assert
        await TestExt.When(async () => {
            var position = await GetReadPosition(alice.Id, chatId);
            position.Should().BeGreaterThanOrEqualTo(entry.LocalId,
                "a single dismissal owes the same read advance as Dismiss all");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MutingShouldNotAdvanceReadPosition()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Muted read advance chat");
        await Tester.InviteToChat(chatId, alice);
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello Alice!");
        await WhenNotified(alice.Id);

        // act
        await Tester.SignIn(alice);
        await Tester.AppServices.UserSettingsUI(Tester.Session).ChatUserSettings(chatId)
            .Update(x => x with { NotificationMode = ChatNotificationMode.Muted }, CancellationToken.None);
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().BeEmpty("a muted chat's notification leaves the active set");
        }, TimeSpan.FromSeconds(10));

        // assert
        // Muting removes the notification through the compute filter rather than by request, and
        // that path must not move the read position - the user never acted on the notification.
        var position = await GetReadPosition(alice.Id, chatId);
        position.Should().BeLessThan(entry.LocalId,
            "a filter-driven removal must not mark the chat read behind the user's back");
    }

    // Private methods

    private async Task<Notification> WhenNotified(UserId userId)
    {
        Notification? notification = null;
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(userId, CancellationToken.None);
            notification = info.Items.Should().ContainSingle().Subject;
        }, TimeSpan.FromSeconds(10));

        return notification!;
    }

    private async Task<long> GetReadPosition(UserId userId, ChatId chatId)
        => (await ChatPositionsBackend.Get(userId, chatId, ChatPositionKind.Read, CancellationToken.None)).EntryLid;
}
