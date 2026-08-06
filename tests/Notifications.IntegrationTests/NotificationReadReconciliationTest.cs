using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationReadReconciliationTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    [Fact]
    public async Task ReadMessageIsHiddenAndReplacedByTheNextOne()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Read reconcile chat");
        await Tester.InviteToChat(chatId, alice);

        // Bob sends a message — Alice should get a notification
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello Alice!");

        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));

        // Alice reads the chat — the notification's entry is now read
        await Tester.Commander.Call(new ChatPositionsBackend_Set(
            alice.Id, chatId, ChatPositionKind.Read, new ChatPosition(entry.LocalId)));

        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Should().BeEmpty("the read message must be hidden by the population re-check");
        }, TimeSpan.FromSeconds(10));

        // A new message arrives — only it should remain displayed
        await Tester.SignIn(bob);
        var entry2 = await Tester.CreateTextEntry(chatId, "Another one");

        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            var displayed = info.Displayed.Should().ContainSingle().Subject;
            displayed.Should().BeOfType<MessageNotification>()
                .Which.EntryLid.Should().Be(entry2.LocalId);
        }, TimeSpan.FromSeconds(10));
    }
}
