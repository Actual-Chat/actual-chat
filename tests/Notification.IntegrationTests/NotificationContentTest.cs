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
        await Tester.React(entry.Id, Emojis.Love);

        // assert
        var aliceNotification = await GetNotification(alice, entry.Id);
        aliceNotification.Should()
            .BeEquivalentTo(
                new Notification(null!) {
                    Title = "Bobby @ Good chat",
                    Content = "Ok!",
                },
                o => o.Text());

        var bobNotification = await GetNotification(bob, entry.Id);
        bobNotification.Should()
            .BeEquivalentTo(
                new Notification(null!) {
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

        var mediaId = await Tester.SaveMedia(chatId, TestImages.GetUploadedImage(TestImages.DefaultJpg));
        var entry = await Tester.CreateTextEntry(chatId, "", mediaId);
        await Tester.SignIn(alice);
        await Tester.React(entry.Id, Emojis.Love);

        // assert
        var aliceNotification = await GetNotification(alice, entry.Id);
        aliceNotification.Should()
            .BeEquivalentTo(
                new Notification(null!) {
                    Title = "Bobby @ Good chat",
                    Content = "Sent an image",
                },
                o => o.Text());

        var bobNotification = await GetNotification(bob, entry.Id);
        bobNotification.Should()
            .BeEquivalentTo(
                new Notification(null!) {
                    Title = "Alice @ Good chat",
                    Content = "❤️ to your image",
                },
                o => o.Text());
    }

    [Fact]
    public async Task ShouldHaveMarbleAvatarIconForGroupChat()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Test Group");
        await Tester.InviteToChat(chatId, alice);

        // act
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello group!");

        // assert
        var notification = await GetNotification(alice, entry.Id);
        notification.IconUrl.Should().NotBeNullOrEmpty();
        notification.IconUrl.Should().Contain("api/avatars/marble/");
        notification.IconUrl.Should().Contain("format=png");
    }

    [Fact]
    public async Task ShouldHaveBeamAvatarIconForPeerChat()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);

        // act
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello peer!");

        // assert
        var notification = await GetNotification(alice, entry.Id);
        notification.IconUrl.Should().NotBeNullOrEmpty();
        notification.IconUrl.Should().Contain("api/avatars/beam/");
        notification.IconUrl.Should().Contain("format=png");
    }

    [Fact]
    public async Task ShouldHaveMarbleAvatarIconForPlaceChat()
    {
        // arrange
        var bob = await Tester.SignInAsBob();
        var place = await Tester.CreatePlace(false, "Test Place");
        var alice = await Tester.SignInAsAlice();
        await Tester.SignIn(bob);
        await Tester.InviteToPlace(place.Id, alice);
        await Tester.SignIn(alice);
        await Tester.JoinPlace(place.Id);
        await Tester.SignIn(bob);
        var (chatId, _) = await Tester.CreateChat(false, "Place Chat", placeId: place.Id);
        await Tester.InviteToChat(chatId, alice);

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Hello place!");

        // assert
        var notification = await GetNotification(alice, entry.Id);
        notification.IconUrl.Should().NotBeNullOrEmpty();
        notification.IconUrl.Should().Contain("api/avatars/marble/");
        notification.IconUrl.Should().Contain("format=png");
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
