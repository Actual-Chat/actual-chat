using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.Users;

namespace ActualChat.Notification.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationContentTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    [Fact]
    public async Task ShouldSendNotificationForReaction()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Good chat");
        await Tester.InviteToChat(chatId, alice);

        // act
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Ok!");
        await Tester.SignIn(alice);
        await Tester.React(entry.Id.ToTextEntryId(), Emoji.RedHeart);

        // assert
        var aliceNotification = await GetNotification(alice, entry.Id);
        aliceNotification.Should()
            .BeEquivalentTo(
                new Notification(NotificationId.None) {
                    Title = "Bob @ Good chat",
                    Content = "Ok!",
                },
                o => o.Text());

        var bobNotification = await GetNotification(bob, entry.Id);
        bobNotification.Should()
            .BeEquivalentTo(
                new Notification(NotificationId.None) {
                    Title = "Alice @ Good chat",
                    Content = "❤️ to \"Ok!\"",
                },
                o => o.Text());
    }

    [Fact]
    public async Task ShouldSendNotificationForReactionOnPhoto()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Good chat");
        await Tester.InviteToChat(chatId, alice);

        // act
        await Tester.SignIn(bob);

        var media = await Tester.Attach(chatId, TestImages.GetUploadedImage(TestImages.DefaultJpg));
        var entry = await Tester.CreateTextEntry(chatId, "", media.Id);
        await Tester.SignIn(alice);
        await Tester.React(entry.Id.ToTextEntryId(), Emoji.RedHeart);

        // assert
        var aliceNotification = await GetNotification(alice, entry.Id);
        aliceNotification.Should()
            .BeEquivalentTo(
                new Notification(NotificationId.None) {
                    Title = "Bob @ Good chat",
                    Content = "Sent an image",
                },
                o => o.Text());

        var bobNotification = await GetNotification(bob, entry.Id);
        bobNotification.Should()
            .BeEquivalentTo(
                new Notification(NotificationId.None) {
                    Title = "Alice @ Good chat",
                    Content = "❤️ to your image",
                },
                o => o.Text());
    }

    private async Task<Notification> GetNotification(AccountFull user, ChatEntryId entryId)
    {
        Notification? notification = null!;
        await TestExt.When(async () => {
            var ids = await Tester.NotificationsBackend.ListRecentNotificationIds(user.Id, Clocks.SystemClock.Now - TimeSpan.FromMinutes(1), CancellationToken.None);
            ids.Should().NotBeEmpty();
            var retrieved = await ids.Select(x => Tester.NotificationsBackend.Get(x, CancellationToken.None)).Collect();
            var notifications = retrieved.SkipNullItems().Where(x => x.EntryId == entryId).ToList();
            notifications.Should().HaveCount(1);
            notification = notifications[0];
        }, TimeSpan.FromSeconds(10));
        return notification;
    }
}
