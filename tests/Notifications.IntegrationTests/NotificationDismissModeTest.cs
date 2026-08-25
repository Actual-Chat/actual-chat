using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationDismissModeTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    [Fact]
    public async Task ReactionShouldSurviveWhenAuthorHasReadTheirOwnEntry()
    {
        // arrange
        // A reaction anchors at the entry it's about - the recipient's own message - so an OnRead
        // reaction dies the moment their Read position covers it.
        var bob = await Tester.SignInAsBob();
        var alice = await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false, "Reaction dismiss-mode chat");
        await Tester.InviteToChat(chatId, bob);
        var entry = await Tester.CreateTextEntry(chatId, "Ok!");
        await SetReadPosition(alice.Id, chatId, entry.LocalId);

        // act
        await Tester.SignIn(bob);
        await Tester.React(entry.Id, Emojis.Love);

        // assert
        await Tester.SignIn(alice);
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            var notification = info.Items.Should()
                .ContainSingle("a read own-entry must not clear an OnView reaction").Subject
                .Should().BeOfType<ReactionNotification>().Subject;
            notification.EntryLid.Should().Be(entry.LocalId);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MessageShouldBeDroppedWhenAuthorHasReadItsEntry()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Message dismiss-mode chat");
        await Tester.InviteToChat(chatId, alice);

        // act
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hi Alice");
        await SetReadPosition(alice.Id, chatId, entry.LocalId);

        // assert
        await Tester.SignIn(alice);
        var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
        info.Items.Should().NotContain(n => n is MessageNotification,
            "OnRead is still what clears an ordinary message");
    }

    [Fact]
    public async Task ExpiredRingShouldNotBeCommitted()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var conversationId = ConversationId.New(TestChatId, 1);
        var stale = CallNotification.New(alice.Id, conversationId, AuthorId.New(TestChatId, 1), false) with {
            Title = "Call",
            Text = "Incoming call",
            SentAt = Moment.Now - Constants.Call.RingTimeout - TimeSpan.FromMinutes(1),
        };

        // act
        await Commander.Call(new NotificationsBackend_Notify(stale));

        // assert
        var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
        info.Items.Should().NotContain(n => n.Id == stale.Id,
            "a ring that outlived its lifetime must not enter the active set");
    }

    [Fact]
    public void DismissModeAndExpiryShouldMatchPolicyPerKind()
    {
        // arrange
        var entryId = ChatEntryId.New(TestChatId, 1);
        var userId = UserId.New();
        var conversationId = ConversationId.New(TestChatId, 1);
        var sentAt = Moment.Now;

        // act
        Notification message = MessageNotification.New(userId, TestChatId, 1) with { SentAt = sentAt };
        Notification mention = MentionNotification.New(userId, entryId) with { SentAt = sentAt };
        Notification conversation = ConversationNotification.New(userId, conversationId, 2) with { SentAt = sentAt };
        Notification reaction = ReactionNotification.New(userId, entryId) with { SentAt = sentAt };
        Notification call = CallNotification.New(userId, conversationId, default, false) with { SentAt = sentAt };
        Notification invitation = InvitationNotification.New(userId, TestChatId) with { SentAt = sentAt };

        // assert
        // OnRead is opt-in, and exactly the kinds GetReadAnchor can resolve opt in.
        message.DismissMode.Should().Be(NotificationDismissMode.OnRead);
        message.ExpiresAt.Should().BeNull();
        mention.DismissMode.Should().Be(NotificationDismissMode.OnRead);
        mention.ExpiresAt.Should().BeNull();
        conversation.DismissMode.Should().Be(NotificationDismissMode.OnRead);
        conversation.ExpiresAt.Should().BeNull();
        reaction.DismissMode.Should().Be(NotificationDismissMode.OnView);
        reaction.ExpiresAt.Should().Be(sentAt + Constants.Notification.ReactionLifespan);

        // Anchorless kinds fall through to the Explicit default rather than declaring it.
        call.DismissMode.Should().Be(NotificationDismissMode.Explicit);
        call.ExpiresAt.Should().Be(
            sentAt + Constants.Call.RingTimeout + Constants.Notification.RingExpirationMargin,
            "the ring's own lifetime plus a margin is what makes a lost cancel recoverable");
        invitation.DismissMode.Should().Be(NotificationDismissMode.Explicit,
            "a kind that declares no mode must not be cleared by a read position it has no anchor for");
    }

    // Private methods

    private Task SetReadPosition(UserId userId, ChatId chatId, long entryLid)
        => Commander.Call(new ChatPositionsBackend_Set(
            userId, chatId, ChatPositionKind.Read, new ChatPosition(entryLid)));
}
