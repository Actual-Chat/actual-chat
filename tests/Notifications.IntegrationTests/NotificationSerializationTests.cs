using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Notifications.IntegrationTests;

public class NotificationSerializationTests(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Session TestSession = Session.New();
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void DeviceSerializationTest()
    {
        var d = new Device("1", DeviceType.AndroidApp, Moment.Now) { AccessedAt = Moment.Now };
        d.AssertPassesThroughAllSerializers();

        d = new Device("1", DeviceType.AndroidApp, Moment.Now);
        d.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void NotificationSerializationTest()
    {
        var userId = UserId.New();
        var d = MessageNotification.New(userId, TestChatId) with {
            Version = 1L,
            Title = "Bob @ Good chat",
            Text = "Sent an image",
            HandledAt = Moment.Now,
        };
        AssertMessagePackRoundtrip(d);

        d = MessageNotification.New(userId, TestChatId) with {
            Version = 1L,
            Title = "Bob @ Good chat",
            Text = "Sent an image",
            HandledAt = null,
        };
        AssertMessagePackRoundtrip(d);
    }

    [Fact]
    public void Notification_PerKind()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var authorId = AuthorId.New(TestChatId, 5);
        foreach (var kind in new[] {
                     NotificationKind.Message, NotificationKind.Reply, NotificationKind.Invitation,
                     NotificationKind.Mention, NotificationKind.Reaction, NotificationKind.Attention,
                     NotificationKind.Thread,
                 }) {
            Notification notification = kind switch {
                NotificationKind.Message => MessageNotification.New(TestUserId, TestChatId, entryId.LocalId, authorId),
                NotificationKind.Reply => ReplyNotification.New(TestUserId, TestChatId, entryId.LocalId, authorId),
                NotificationKind.Thread => ThreadNotification.New(TestUserId, TestChatId, entryId.LocalId, authorId),
                NotificationKind.Invitation => InvitationNotification.New(TestUserId, TestChatId, authorId),
                NotificationKind.Mention => MentionNotification.New(TestUserId, entryId, authorId),
                NotificationKind.Reaction => ReactionNotification.New(TestUserId, entryId, authorId),
                NotificationKind.Attention => AttentionNotification.New(TestUserId, entryId, authorId),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            notification = notification with {
                Version = 1,
                Title = "Test",
                Text = "Content",
            };
            AssertMessagePackRoundtrip(notification);
        }
    }

    [Fact]
    public void ConversationNotification_Basic()
    {
        var conversationId = ConversationId.New(TestChatId, 2067);
        var notification = ConversationNotification.New(TestUserId, conversationId, 2100) with {
            Version = 1,
            Title = "Good chat",
            Text = "Voice chat: weekend plans",
        };

        notification.ChatId.Should().Be(TestChatId);
        notification.StartEntryLid.Should().Be(2067);
        notification.EndEntryLid.Should().Be(2100);
        notification.SimilarityKey.Should().Be(conversationId.Value);
        notification.Kind.Should().Be(NotificationKind.Conversation);
        AssertMessagePackRoundtrip(notification);
    }

    [Fact]
    public void ConversationNotification_ChatTagAndLink()
    {
        var conversationId = ConversationId.New(TestChatId, 2067);
        var notification = ConversationNotification.New(TestUserId, conversationId, 2100);

        // The tag groups under the chat banner — NOT the raw "chatId:lid" similarity key.
        notification.GetChatTag().Should().Be(TestChatId.Value);
        notification.GetChatLink().Should().Be(Links.Chat(ChatEntryId.New(TestChatId, 2067)));
    }

    [Fact]
    public void ExplicitNotification_Basic()
    {
        var id = ExplicitNotificationId.New(
            TestUserId,
            ExplicitNotificationKind.NotifyMentionedMembers,
            "the-actual-one:0:2067");
        var notification = new ExplicitNotification(id) {
            CreatedAt = new Moment(DateTime.UtcNow),
        };

        var s = notification.PassThroughAllSerializers(Out);
        s.Id.Should().Be(notification.Id);
        s.CreatedAt.Should().Be(notification.CreatedAt);
    }

    [Fact]
    public void SerializeUpsertExplicitNotificationCommandTest()
    {
        var explicitNotificationId = ExplicitNotificationId.New(
            UserId.Parse("9EV1f3"),
            ExplicitNotificationKind.NotifyMentionedMembers,
            "the-actual-one:0:2067");
        var notification = new ExplicitNotification(explicitNotificationId);
        var command = new NotificationsBackend_UpsertExplicitNotification(notification);
        var commandJson = DbOperation.Serializer.Write(command);
        commandJson.Should().NotBeNullOrWhiteSpace();
    }

    // API Commands

    [Fact]
    public void Notifications_Handle_Basic()
    {
        var id = NotificationId.New(TestUserId, NotificationKind.Message, "1234");
        var cmd = new Notifications_Handle(TestSession, id);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Notifications_RegisterDevice_Basic()
    {
        var cmd = new Notifications_RegisterDevice(TestSession, "device-1", DeviceType.AndroidApp);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Notifications_DeregisterDevice_Basic()
    {
        var cmd = new Notifications_DeregisterDevice(TestSession, "device-1");
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Notifications_NotifyMembers_Basic()
    {
        var cmd = new Notifications_NotifyMembers(TestSession, TestChatId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Notifications_NotifyMentionedMembers_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var cmd = new Notifications_NotifyMentionedMembers(TestSession, entryId);
        cmd.AssertPassesThroughAllSerializers();
    }

    // Backend Commands

    [Fact]
    public void NotificationsBackend_Notify_Basic()
    {
        var notification = MessageNotification.New(TestUserId, TestChatId) with { Version = 1, Title = "Test" };
        var cmd = new NotificationsBackend_Notify(notification);
        var deserialized = AssertMessagePackRoundtrip(cmd);
        deserialized.Notification.Id.Should().Be(cmd.Notification.Id);
    }

    private T AssertMessagePackRoundtrip<T>(T value)
    {
        var result = value.PassThroughMessagePackByteSerializer(Out);
        result.Should().Be(value);
        return result;
    }

    [Fact]
    public void NotificationsBackend_RegisterDevice_Basic()
    {
        var cmd = new NotificationsBackend_RegisterDevice(TestUserId, "device-1", DeviceType.AndroidApp, "session-hash");
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void NotificationsBackend_RemoveDevices_Basic()
    {
        Symbol[] deviceIds = ["device-1", "device-2"];
        var cmd = new NotificationsBackend_RemoveDevices(deviceIds);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.DeviceIds.Length.Should().Be(original.DeviceIds.Length);
            }, Out);
    }

    [Fact]
    public void NotificationsBackend_RemoveAccount_Basic()
    {
        var cmd = new NotificationsBackend_RemoveAccount(TestUserId);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void NotificationsBackend_NotifyMembers_Basic()
    {
        var cmd = new NotificationsBackend_NotifyMembers(TestUserId, TestChatId, 42);
        cmd.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void NotificationsBackend_NotifyMentionedMembers_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var cmd = new NotificationsBackend_NotifyMentionedMembers(TestUserId, entryId, [TestUserId]);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.UserId.Should().Be(original.UserId);
                deserialized.ChatEntryId.Should().Be(original.ChatEntryId);
            }, Out);
    }

    [Fact]
    public void NotificationsBackend_NotifyConversation_Basic()
    {
        var conversationId = ConversationId.New(TestChatId, 2067);
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new NotificationsBackend_NotifyConversation(
            conversationId, ConversationNotificationPhase.Titled, "Voice chat: plans", 2100, [authorId]);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.ConversationId.Should().Be(original.ConversationId);
                deserialized.Phase.Should().Be(original.Phase);
                deserialized.EndEntryLid.Should().Be(original.EndEntryLid);
                deserialized.AuthorIds.Should().Equal(original.AuthorIds);
            }, Out);
    }
}
