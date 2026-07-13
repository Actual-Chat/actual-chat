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
