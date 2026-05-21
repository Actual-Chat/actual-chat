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
        var id = NotificationId.New(UserId.New(), NotificationKind.Message, "1234");
        var d = new Notification(id, 1L) {
            Title = "Bob @ Good chat",
            Content = "Sent an image",
            HandledAt = Moment.Now,
        };
        d.AssertPassesThroughAllSerializers();

        d = new Notification(id, 1L) {
            Title = "Bob @ Good chat",
            Content = "Sent an image",
            HandledAt = null,
        };
        d.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Notification_WithChatNotificationOption()
    {
        var id = NotificationId.New(TestUserId, NotificationKind.Message, "1234");
        var notification = new Notification(id, 1) {
            Title = "Test",
            Content = "Content",
            ChatNotification = new ChatNotificationOption(TestChatId),
        };
        notification.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Notification_WithChatEntryNotificationOption()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var authorId = AuthorId.New(TestChatId, 5);
        var id = NotificationId.New(TestUserId, NotificationKind.Message, "1234");
        var notification = new Notification(id, 1) {
            Title = "Test",
            Content = "Content",
            ChatEntryNotification = new ChatEntryNotificationOption(entryId, authorId),
        };
        notification.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Notification_WithGetAttentionNotificationOption()
    {
        var callerId = AuthorId.New(TestChatId, 5);
        var id = NotificationId.New(TestUserId, NotificationKind.Message, "1234");
        var notification = new Notification(id, 1) {
            Title = "Test",
            Content = "Content",
            GetAttentionNotification = new GetAttentionNotificationOption(TestChatId, callerId, 42),
        };
        notification.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ChatNotificationOption_Basic()
    {
        var option = new ChatNotificationOption(TestChatId);
        option.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ChatEntryNotificationOption_Basic()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var authorId = AuthorId.New(TestChatId, 5);
        var option = new ChatEntryNotificationOption(entryId, authorId);
        option.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void GetAttentionNotificationOption_Basic()
    {
        var callerId = AuthorId.New(TestChatId, 5);
        var option = new GetAttentionNotificationOption(TestChatId, callerId, 42);
        option.AssertPassesThroughAllSerializers();
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
        var id = NotificationId.New(TestUserId, NotificationKind.Message, "1234");
        var notification = new Notification(id, 1) { Title = "Test" };
        var cmd = new NotificationsBackend_Notify(notification);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Notification.Id.Should().Be(original.Notification.Id);
            }, Out);
    }

    [Fact]
    public void NotificationsBackend_Upsert_Basic()
    {
        var id = NotificationId.New(TestUserId, NotificationKind.Message, "1234");
        var notification = new Notification(id, 1) { Title = "Test" };
        var cmd = new NotificationsBackend_Upsert(notification);
        cmd.AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                deserialized.Notification.Id.Should().Be(original.Notification.Id);
            }, Out);
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
}
