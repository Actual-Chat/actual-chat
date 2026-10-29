using ActualChat.Localization;
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationRenamedContactTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string BobRenamed = "B YC";

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    [Fact]
    public async Task PeerMessageShouldBeTitledWithTheNameTheReaderGaveTheSender()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        await RenameContact(alice, bob, BobRenamed);
        var chatId = PeerChatId.New(alice.Id, bob.Id);

        // act
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello peer!");

        // assert
        var notification = await GetNotification<MessageNotification>(alice, n => n.EntryLid == entry.LocalId);
        notification.Title.Should().Be(BobRenamed);
        notification.SenderName.Should().Be(BobRenamed);
        notification.RecentMessages.Should().ContainSingle()
            .Which.AuthorName.Should().Be(BobRenamed);
    }

    [Fact]
    public async Task GroupMessageShouldNameTheSenderTheWayEachReaderDoes()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var accounts = await Tester.CreateAccounts(1);
        var carol = accounts[0];
        var bob = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Renamed sender chat");
        await JoinChat(alice, chatId, inviteId);
        await JoinChat(carol, chatId, inviteId);
        await RenameContact(alice, bob, BobRenamed);

        // act
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hi all");

        // assert
        var aliceNotification = await GetNotification<MessageNotification>(alice, n => n.EntryLid == entry.LocalId);
        aliceNotification.Title.Should().Be("Renamed sender chat");
        aliceNotification.SenderName.Should().Be(BobRenamed);
        aliceNotification.Text.Should().Be($"{BobRenamed}: Hi all");

        var carolNotification = await GetNotification<MessageNotification>(carol, n => n.EntryLid == entry.LocalId);
        carolNotification.SenderName.Should().Be("Bobby", "only Alice renamed Bob");
        carolNotification.Text.Should().Be("Bobby: Hi all");
    }

    [Fact]
    public async Task AttentionRequestShouldNameTheSenderTheWayTheReaderDoes()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Attention chat");
        await JoinChat(alice, chatId, inviteId);
        await RenameContact(alice, bob, BobRenamed);

        // act
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Everyone, look here");
        var notifyMembersCmd = new NotificationsBackend_NotifyMembers(bob.Id, chatId, entry.LocalId);
        await Tester.Commander.Call(notifyMembersCmd);

        // assert
        var attention = await GetNotification<AttentionNotification>(alice, _ => true);
        attention.Title.Should().Be($"{BobRenamed} @ Attention chat");
        attention.SenderName.Should().Be(BobRenamed);
        attention.Text.Should().Be($"{BobRenamed} asks for attention");
    }

    [Fact]
    public async Task IncomingCallShouldNameTheCallerTheWayTheInviteeDoes()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Renamed caller chat");
        var bobAuthor = await Tester.GetOwnAuthor(chatId).Require();
        var aliceAuthor = await JoinChat(alice, chatId, inviteId);
        await RenameContact(alice, bob, BobRenamed);

        // act
        var notifyCallCmd = new NotificationsBackend_NotifyCall(
            CallId.New(chatId, "1"), bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false);
        await Commander.Call(notifyCallCmd);

        // assert
        var ring = await GetNotification<CallNotification>(alice, _ => true);
        ring.Title.Should().Be($"{BobRenamed} @ Renamed caller chat");
        ring.SenderName.Should().Be(BobRenamed);
    }

    [Fact]
    public async Task VoiceChatStartedShouldNameTheAuthorTheWayTheReaderDoes()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        var (chatId, inviteId) = await Tester.CreateChat(false, "Renamed voice chat");
        await JoinChat(alice, chatId, inviteId);
        await RenameContact(alice, bob, BobRenamed);
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Let's talk");

        // act
        var notifyCmd = new NotificationsBackend_NotifyConversation(
            ConversationId.New(chatId, entry.LocalId), ConversationNotificationPhase.Started,
            "", entry.LocalId, [entry.AuthorId]);
        await Commander.Call(notifyCmd);

        // assert
        var english = LanguageStringLocalizer.Get(Languages.English);
        var conversation = await GetNotification<ConversationNotification>(alice, _ => true);
        conversation.Text.Should().Be(NotificationHelper.GetVoiceChatStartedText([BobRenamed], english));
    }

    // Private methods

    private async Task RenameContact(AccountFull owner, AccountFull other, string name)
    {
        await Tester.SignIn(owner);
        await Tester.RenamePeerContact(owner, other, name);
    }

    private async Task<AuthorFull> JoinChat(AccountFull account, ChatId chatId, string inviteId)
    {
        await Tester.SignIn(account);
        await Tester.JoinChat(chatId, inviteId);
        return await Tester.GetOwnAuthor(chatId).Require();
    }

    private async Task<T> GetNotification<T>(AccountFull user, Func<T, bool> predicate)
        where T : Notification
    {
        T notification = null!;
        await TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(user.Id, CancellationToken.None);
            notification = info.Items.OfType<T>().Where(predicate).Should().ContainSingle().Subject;
        }, TimeSpan.FromSeconds(15));
        return notification;
    }
}
