using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Notifications.IntegrationTests;

public class NotificationSerializationTests(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Session TestSession = Session.New();
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void DeviceShouldPassThroughSerializers()
    {
        var d = new Device("1", DeviceType.AndroidApp, Moment.Now) { AccessedAt = Moment.Now };
        d.AssertPassesThroughSerializers();

        d = new Device("1", DeviceType.AndroidApp, Moment.Now);
        d.AssertPassesThroughSerializers();
    }

    [Fact]
    public void NotificationShouldRoundtripThroughMessagePack()
    {
        var userId = UserId.New();
        var d = MessageNotification.New(userId, TestChatId) with {
            Version = 1L,
            Title = "Bob @ Good chat",
            Text = "Sent an image",
            SentAt = Moment.Now,
        };
        AssertMessagePackRoundtrip(d);

        d = MessageNotification.New(userId, TestChatId) with {
            Version = 1L,
            Title = "Bob @ Good chat",
            Text = "Sent an image",
        };
        AssertMessagePackRoundtrip(d);
    }

    [Fact]
    public void BeepGroupShouldSurviveRoundtrip()
    {
        // arrange
        var authorId = AuthorId.New(TestChatId, 5);
        var notification = MessageNotification.New(TestUserId, TestChatId, 100, authorId) with {
            Version = 1,
            Title = "Bob @ Good chat",
            Text = "Sent a voice message",
            BeepGroup = "a:" + authorId.Value,
            LastBeepGroup = "a:" + AuthorId.New(TestChatId, 6).Value,
        };

        // act
        var deserialized = AssertMessagePackRoundtrip(notification);

        // assert
        deserialized.BeepGroup.Should().Be(notification.BeepGroup);
        deserialized.LastBeepGroup.Should().Be(notification.LastBeepGroup);
    }

    [Fact]
    public void NotificationShouldRoundtripPerKind()
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
    public void NotificationShouldWriteJsonPerKind()
    {
        // /test/notifications renders the active set with SystemJsonSerializer against each
        // notification's concrete type - write-only, so this covers the page, not a roundtrip.
        var entryId = ChatEntryId.New(TestChatId, 1);
        var authorId = AuthorId.New(TestChatId, 5);
        var conversationId = ConversationId.New(TestChatId, 2067);
        Notification[] notifications = [
            MessageNotification.New(TestUserId, TestChatId, entryId.LocalId, authorId),
            ReplyNotification.New(TestUserId, TestChatId, entryId.LocalId, authorId),
            ThreadNotification.New(TestUserId, TestChatId, entryId.LocalId, authorId),
            InvitationNotification.New(TestUserId, TestChatId, authorId),
            MentionNotification.New(TestUserId, entryId, authorId),
            ReactionNotification.New(TestUserId, entryId, authorId),
            AttentionNotification.New(TestUserId, entryId, authorId),
            ConversationNotification.New(TestUserId, conversationId, 2100),
            CallNotification.New(TestUserId, conversationId, authorId, true),
        ];
        foreach (var n in notifications) {
            var notification = n with {
                Version = 1,
                Title = "Test",
                Text = "Content",
                SentAt = Moment.Now,
            };
            var json = SystemJsonSerializer.Pretty.Write(notification, notification.GetType());
            Out.WriteLine($"{notification.Kind}: {json}");
            json.Should().Contain(notification.Id.Value);
            json.Should().Contain("Test");
        }
    }

    [Fact]
    public void ConversationNotificationShouldExposeItsAnchors()
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
    public void ConversationNotificationShouldTagAndLinkToItsChat()
    {
        var conversationId = ConversationId.New(TestChatId, 2067);
        var notification = ConversationNotification.New(TestUserId, conversationId, 2100);

        // The tag groups under the chat banner — NOT the raw "chatId:lid" similarity key.
        notification.GetChatTag().Should().Be(TestChatId.Value);
        notification.GetChatLink().Should().Be(Links.Chat(ChatEntryId.New(TestChatId, 2067)));
    }

    [Fact]
    public void PushTagShouldBePerEntryForIndividuallySeenKinds()
    {
        var entryId = ChatEntryId.New(TestChatId, 2067);

        // Individually-seen kinds keep their own banner: the tag is the entry, so a later
        // message (or a second mention) must not replace an unread mention's banner.
        MentionNotification.New(TestUserId, entryId).GetPushTag().Should().Be(entryId.Value);
        AttentionNotification.New(TestUserId, entryId).GetPushTag().Should().Be(entryId.Value);
        ReactionNotification.New(TestUserId, entryId).GetPushTag().Should().Be(entryId.Value);

        var otherEntryId = ChatEntryId.New(TestChatId, 2100);
        MentionNotification.New(TestUserId, otherEntryId).GetPushTag()
            .Should().NotBe(MentionNotification.New(TestUserId, entryId).GetPushTag());
    }

    [Fact]
    public void AccumulatedReactorsShouldSurviveRoundtrip()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var entryId = ChatEntryId.New(TestChatId, 1);
        var notification = ReactionNotification.New(TestUserId, entryId, kate) with {
            Version = 1,
            Title = "Kate",
            Text = "reacted to your message",
            AuthorIds = ApiArray.New(bob, kate),
            Emojis = ApiArray.New(Emojis.Awesome, Emojis.Party),
        };

        // act
        // Not AssertMessagePackRoundtrip: it compares whole records, and ApiArray is
        // reference-compared, so a populated one never equals its deserialized copy.
        var deserialized = notification.PassThroughMessagePackByteSerializer(Out);

        // assert
        deserialized.AuthorIds.Should().Equal([bob, kate]);
        deserialized.Emojis.Should().Equal([Emojis.Awesome, Emojis.Party]);
    }

    [Fact]
    public void ReactionWithoutAccumulatedReactorsShouldRoundtrip()
    {
        // arrange
        // The shape a pre-accumulation blob has: keys 9 and 10 absent.
        var entryId = ChatEntryId.New(TestChatId, 1);
        var notification = new ReactionNotification(
            NotificationId.New(TestUserId, NotificationKind.Reaction, entryId.Value)) with {
            Version = 1,
            Title = "Kate",
            Text = "reacted to your message",
        };

        // act
        var deserialized = AssertMessagePackRoundtrip(notification);

        // assert
        deserialized.AuthorIds.Should().BeEmpty();
        deserialized.Emojis.Should().BeEmpty();
        deserialized.Title.Should().Be("Kate");
    }

    [Fact]
    public void PushTagShouldBePerChatForCoalescingKinds()
    {
        MessageNotification.New(TestUserId, TestChatId, 2067).GetPushTag().Should().Be(TestChatId.Value);
        ReplyNotification.New(TestUserId, TestChatId, 2067).GetPushTag().Should().Be(TestChatId.Value);
        ThreadNotification.New(TestUserId, TestChatId, 2067).GetPushTag().Should().Be(TestChatId.Value);

        var conversationId = ConversationId.New(TestChatId, 2067);
        ConversationNotification.New(TestUserId, conversationId, 2100).GetPushTag().Should().Be(TestChatId.Value);
    }

    [Fact]
    public void ExplicitNotificationShouldPassThroughSerializers()
    {
        var id = ExplicitNotificationId.New(
            TestUserId,
            ExplicitNotificationKind.NotifyMentionedMembers,
            "the-actual-one:0:2067");
        var notification = new ExplicitNotification(id) {
            CreatedAt = new Moment(DateTime.UtcNow),
        };

        var s = notification.PassThroughSerializers(Out);
        s.Id.Should().Be(notification.Id);
        s.CreatedAt.Should().Be(notification.CreatedAt);
    }

    [Fact]
    public void UpsertExplicitNotificationCommandShouldSerialize()
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
    public void DismissCommandShouldPassThroughSerializers()
    {
        var id = NotificationId.New(TestUserId, NotificationKind.Message, "1234");
        var cmd = new Notifications_Dismiss { Session = TestSession, NotificationId = id };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void RegisterDeviceCommandShouldPassThroughSerializers()
    {
        var cmd = new Notifications_RegisterDevice {
            Session = TestSession,
            DeviceId = "device-1",
            DeviceType = DeviceType.AndroidApp,
        };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void DeregisterDeviceCommandShouldPassThroughSerializers()
    {
        var cmd = new Notifications_DeregisterDevice { Session = TestSession, DeviceId = "device-1" };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void NotifyMembersCommandShouldPassThroughSerializers()
    {
        var cmd = new Notifications_NotifyMembers { Session = TestSession, ChatId = TestChatId };
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void NotifyMentionedMembersCommandShouldPassThroughSerializers()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var cmd = new Notifications_NotifyMentionedMembers { Session = TestSession, ChatEntryId = entryId };
        cmd.AssertPassesThroughSerializers();
    }

    // Backend Commands

    [Fact]
    public void BackendNotifyCommandShouldRoundtrip()
    {
        var notification = MessageNotification.New(TestUserId, TestChatId) with { Version = 1, Title = "Test" };
        var cmd = new NotificationsBackend_Notify(notification);
        var deserialized = AssertMessagePackRoundtrip(cmd);
        deserialized.Notification.Id.Should().Be(cmd.Notification.Id);
    }

    [Fact]
    public void BackendRegisterDeviceCommandShouldPassThroughSerializers()
    {
        var cmd = new NotificationsBackend_RegisterDevice(TestUserId, "device-1", DeviceType.AndroidApp, "session-hash");
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void BackendRemoveDevicesCommandShouldPassThroughSerializers()
    {
        Symbol[] deviceIds = ["device-1", "device-2"];
        var cmd = new NotificationsBackend_RemoveDevices(deviceIds);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.DeviceIds.Length.Should().Be(original.DeviceIds.Length);
            }, Out);
    }

    [Fact]
    public void BackendRemoveAccountCommandShouldPassThroughSerializers()
    {
        var cmd = new NotificationsBackend_RemoveAccount(TestUserId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void BackendNotifyMembersCommandShouldPassThroughSerializers()
    {
        var cmd = new NotificationsBackend_NotifyMembers(TestUserId, TestChatId, 42);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void BackendNotifyMentionedMembersCommandShouldPassThroughSerializers()
    {
        var entryId = ChatEntryId.New(TestChatId, 1);
        var cmd = new NotificationsBackend_NotifyMentionedMembers(TestUserId, entryId, [TestUserId]);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.UserId.Should().Be(original.UserId);
                deserialized.ChatEntryId.Should().Be(original.ChatEntryId);
            }, Out);
    }

    [Fact]
    public void BackendNotifyConversationCommandShouldPassThroughSerializers()
    {
        var conversationId = ConversationId.New(TestChatId, 2067);
        var authorId = AuthorId.New(TestChatId, 5);
        var cmd = new NotificationsBackend_NotifyConversation(
            conversationId, ConversationNotificationPhase.Titled, "Voice chat: plans", 2100, [authorId]);
        cmd.AssertPassesThroughSerializers(
            (deserialized, original) => {
                deserialized.ConversationId.Should().Be(original.ConversationId);
                deserialized.Phase.Should().Be(original.Phase);
                deserialized.EndEntryLid.Should().Be(original.EndEntryLid);
                deserialized.AuthorIds.Should().Equal(original.AuthorIds);
            }, Out);
    }

    // Private methods

    private T AssertMessagePackRoundtrip<T>(T value)
    {
        var result = value.PassThroughMessagePackByteSerializer(Out);
        result.Should().Be(value);
        return result;
    }
}
