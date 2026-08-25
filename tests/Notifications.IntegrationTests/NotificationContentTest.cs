using ActualChat.Testing.Host;
using ActualChat.Uploads;

namespace ActualChat.Notifications.IntegrationTests;

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
        var (chat, _) = await Tester.CreateAndGetChat(false, "Good chat");
        await Tester.InviteToChat(chat.Id, alice);

        // act
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chat.Id, "Ok!");
        await Tester.SignIn(alice);
        await Tester.React(entry.Id, Emojis.Love);

        // assert
        var aliceNotification = await GetNotification(alice, entry.Id);
        aliceNotification.Title.Should().Be($"Bobby @ {chat.Title}");
        aliceNotification.Text.Should().Be("Bobby: Ok!");

        var bobNotification = await GetNotification(bob, entry.Id);
        bobNotification.Title.Should().Be($"Alice @ {chat.Title}");
        bobNotification.Text.Should().Be("❤️ to \"Ok!\"");
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
        aliceNotification.Title.Should().Be("Bobby @ Good chat");
        aliceNotification.Text.Should().Be("Bobby: Sent an image");

        var bobNotification = await GetNotification(bob, entry.Id);
        bobNotification.Title.Should().Be("Alice @ Good chat");
        bobNotification.Text.Should().Be("❤️ to your image");
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

    [Fact]
    public async Task ShouldUseCustomIconForGroupChatWithPicture()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Chat With Icon");
        await Tester.InviteToChat(chatId, alice);

        // Set custom picture on chat
        var mediaId = await Tester.SaveMedia(chatId, TestImages.GetUploadedImage(TestImages.DefaultJpg));
        await Tester.SetChatMedia(chatId, mediaId);

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Hello with custom icon!");

        // assert
        var notification = await GetNotification(alice, entry.Id);
        notification.IconUrl.Should().NotBeNullOrEmpty();
        // Should use content URL for custom picture, not generated avatar
        notification.IconUrl.Should().NotContain("api/avatars/");
        notification.IconUrl.Should().Contain("api/content/");
    }

    [Fact]
    public async Task ShouldUseCustomAvatarForPeerChatWithUserPicture()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        var ownAccount = await Tester.Accounts.GetOwn(Tester.Session, default);

        // Save media with user scope (for avatar pictures)
        var mediaSaver = Tester.AppServices.GetRequiredService<IMediaSaver>();
        var mediaId = MediaId.New(ownAccount.Id.Value);
        await mediaSaver.Save(mediaId, TestImages.GetUploadedImage(TestImages.DefaultJpg), null, MediaKind.UserAvatarPicture, default);

        // Create avatar with custom picture for bob
        var avatarWithPicture = await Tester.Commander.Call(new Avatars_Change(
            Tester.Session,
            Symbol.Empty,
            null,
            Change.Create(new AvatarDiff {
                Name = "Bob with Picture",
                MediaId = Option.Some<MediaId?>(mediaId),
            })));

        // Set as default avatar
        await Tester.Commander.Call(new Avatars_SetDefault(Tester.Session, avatarWithPicture.Id));

        var chatId = PeerChatId.New(alice.Id, bob.Id);

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Hello with custom avatar!");

        // assert
        var notification = await GetNotification(alice, entry.Id);
        notification.IconUrl.Should().NotBeNullOrEmpty();
        // Should use content URL for custom avatar, not generated avatar
        notification.IconUrl.Should().NotContain("api/avatars/");
        notification.IconUrl.Should().Contain("api/content/");
    }

    private async Task<Notification> GetNotification(AccountFull user, ChatEntryId entryId)
    {
        Notification notification = null!;
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(user.Id, CancellationToken.None);
            var notifications = info.Items
                .Where(n => EntryIdOf(n) == entryId)
                .ToList();
            notifications.Should().HaveCount(1);
            notification = notifications[0];
        }, TimeSpan.FromSeconds(10));
        return notification;

        static ChatEntryId? EntryIdOf(Notification n) => n switch {
            ChatEntryNotification e => e.EntryId,
            ChatEntryRelatedNotification r => r.EntryId,
            _ => null,
        };
    }
}
