namespace ActualChat.Notifications.IntegrationTests;

public class CallNotificationTagTests(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void PushTagIsCallScoped()
    {
        var conversationId = ConversationId.New(TestChatId, 2067);
        var caller = AuthorId.New(TestChatId, 5);
        var ring = CallNotification.New(TestUserId, conversationId, caller, hasVideo: false);

        ring.GetPushTag().Should().Be("call-" + TestChatId.Value);
        ring.GetChatTag().Should().Be(TestChatId.Value);
    }

    [Fact]
    public void AttentionPushTagIsEntryScopedNotChatScoped()
    {
        // arrange
        // AttentionNotification extends ChatEntryNotification, so it tags by entry like a mention.
        // The Android dismissal path has to map that back to a chat to clear ChatAttentionService's
        // request, and an entry tag does not parse as a ChatId - which is the trap this pins.
        var entryId = ChatEntryId.New(TestChatId, 7);
        var attention = AttentionNotification.New(TestUserId, entryId);

        // act
        var tag = attention.GetPushTag();

        // assert
        tag.Should().Be(entryId.Value);
        ChatId.TryParse(tag, out _).Should().BeFalse("an entry tag is not a chat id");
        ChatEntryId.TryParse(tag, out var parsed).Should().BeTrue();
        parsed.ChatId.Should().Be(TestChatId);
    }

    [Fact]
    public void DismissalSharesRingTag()
    {
        var conversationId = ConversationId.New(TestChatId, 2067);
        var caller = AuthorId.New(TestChatId, 5);
        var ring = CallNotification.New(TestUserId, conversationId, caller, hasVideo: true);
        var dismissal = new CallNotification(
            NotificationId.New(TestUserId, NotificationKind.IncomingCall, conversationId.Value));

        dismissal.GetPushTag().Should().Be(ring.GetPushTag());
        dismissal.GetPushTag().Should().NotBeNull();
    }
}
