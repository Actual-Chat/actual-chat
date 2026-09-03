using ActualChat.Notifications.Db;

namespace ActualChat.Notifications.IntegrationTests;

// Notifications are array-form MessagePack, so a slot below the chain's max key that a member
// didn't exist for yet is written as nil, and a member above it is simply absent. v2.18 added
// keys 9..12 to ReactionNotification (nil in every v2.17 row) and 21..22 to ChatNotification
// (absent in v2.17 rows). The Legacy* records mirror the v2.17 layout, so the tests produce
// exactly the bytes v2.17 pods wrote and v2.17 clients still read.
public sealed class NotificationCompatibilityTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly ChatEntryId TestEntryId = ChatEntryId.New(TestChatId, 1);
    private static readonly AuthorId Bob = AuthorId.New(TestChatId, 5);
    private static readonly AuthorId Kate = AuthorId.New(TestChatId, 6);
    private static readonly Moment TestSentAt = Moment.EpochStart + TimeSpan.FromSeconds(1);
    private static readonly MessagePackSerializerOptions Options = MessagePackByteSerializer.DefaultOptions;

    [Fact]
    public void ServerShouldReadReactionRowWrittenByPreviousRelease()
    {
        // arrange
        var legacy = NewLegacyReaction();
        var row = NewLegacyRow(legacy);

        // act
        var info = row.ToModel();

        // assert
        var reaction = info.Items.Should().ContainSingle().Which.Should().BeOfType<ReactionNotification>().Which;
        reaction.Id.Should().Be(legacy.Id);
        reaction.Title.Should().Be("Bob");
        reaction.AuthorId.Should().Be(Bob);
        reaction.SentAt.Should().Be(TestSentAt);
        reaction.AuthorIds.IsEmpty.Should().BeTrue();
        reaction.Emojis.IsEmpty.Should().BeTrue();
        reaction.QuotedText.Should().Be("");
        reaction.LastEmoji.Should().BeNull();
        reaction.SenderName.Should().Be("");
        reaction.GroupTitle.Should().Be("");
    }

    [Fact]
    public void ServerShouldReadMessageRowWrittenByPreviousRelease()
    {
        // arrange
        var legacy = NewLegacyMessage();
        var row = NewLegacyRow(legacy);

        // act
        var info = row.ToModel();

        // assert
        var message = info.Items.Should().ContainSingle().Which.Should().BeOfType<MessageNotification>().Which;
        message.Id.Should().Be(legacy.Id);
        message.Title.Should().Be("Bob");
        message.AuthorId.Should().Be(Bob);
        message.EntryLid.Should().Be(7);
        message.UnreadCount.Should().Be(2);
        message.AuthorIds.Should().Equal([Bob, Kate]);
        message.RecentMessages.Should().HaveCount(1);
        message.SenderName.Should().Be("");
        message.GroupTitle.Should().Be("");
    }

    [Fact]
    public void PreviousReleaseClientShouldReadCurrentReaction()
    {
        // arrange
        var info = NewInfo(NewCurrentReaction());

        // act
        var legacy = PassThroughToLegacy(info);

        // assert
        var item = legacy.Items.Should().ContainSingle().Which;
        var reaction = item.Should().BeOfType<LegacyReactionNotification>().Which;
        reaction.Id.Should().Be(info.Items[0].Id);
        reaction.Title.Should().Be("Bob");
        reaction.AuthorId.Should().Be(Bob);
        reaction.SentAt.Should().Be(TestSentAt);
    }

    [Fact]
    public void PreviousReleaseClientShouldReadCurrentMessage()
    {
        // arrange
        var info = NewInfo(NewCurrentMessage());

        // act
        var legacy = PassThroughToLegacy(info);

        // assert
        var item = legacy.Items.Should().ContainSingle().Which;
        var message = item.Should().BeOfType<LegacyMessageNotification>().Which;
        message.Id.Should().Be(info.Items[0].Id);
        message.Title.Should().Be("Bob");
        message.AuthorId.Should().Be(Bob);
        message.EntryLid.Should().Be(7);
        message.UnreadCount.Should().Be(2);
        message.AuthorIds.Should().Equal([Bob, Kate]);
        message.RecentMessages.Should().HaveCount(1);
    }

    [Fact]
    public void CurrentReactionRowShouldRoundTrip()
    {
        // arrange
        var row = new DbUserNotifications();

        // act
        row.UpdateFrom(NewInfo(NewCurrentReaction()));
        var reaction = (ReactionNotification)row.ToModel().Items[0];

        // assert
        reaction.AuthorIds.Should().Equal([Bob, Kate]);
        reaction.Emojis.Should().Equal([Emojis.Awesome, Emojis.Party]);
        reaction.QuotedText.Should().Be("hello");
        reaction.LastEmoji.Should().Be(Emojis.Party);
        reaction.SenderName.Should().Be("Bob");
        reaction.GroupTitle.Should().Be("The actual one");
    }

    [Fact]
    public void CurrentMessageRowShouldRoundTrip()
    {
        // arrange
        var row = new DbUserNotifications();

        // act
        row.UpdateFrom(NewInfo(NewCurrentMessage()));
        var message = (MessageNotification)row.ToModel().Items[0];

        // assert
        message.EntryLid.Should().Be(7);
        message.UnreadCount.Should().Be(2);
        message.AuthorIds.Should().Equal([Bob, Kate]);
        message.RecentMessages.Should().HaveCount(1);
        message.SenderName.Should().Be("Bob");
        message.GroupTitle.Should().Be("The actual one");
    }

    // Private methods

    private static UserNotificationInfo NewInfo(Notification notification)
        => new(TestUserId, 1) { Items = ApiArray.New(notification) };

    private static DbUserNotifications NewLegacyRow(LegacyNotification notification)
    {
        var legacy = new LegacyUserNotificationInfo(TestUserId, 1) {
            Items = ApiArray.New(notification),
        };
        return new DbUserNotifications {
            Id = TestUserId.Value,
            Version = 1,
            ItemsData = [0, ..MessagePackSerializer.Serialize(legacy, Options)],
        };
    }

    private static LegacyUserNotificationInfo PassThroughToLegacy(UserNotificationInfo info)
    {
        var bytes = MessagePackSerializer.Serialize(info, Options);
        return MessagePackSerializer.Deserialize<LegacyUserNotificationInfo>(bytes, Options);
    }

    private static ReactionNotification NewCurrentReaction()
        => ReactionNotification.New(TestUserId, TestEntryId, Bob) with {
            Title = "Bob",
            Text = "🥳 to hello",
            SentAt = TestSentAt,
            SenderName = "Bob",
            GroupTitle = "The actual one",
            AuthorIds = ApiArray.New(Bob, Kate),
            Emojis = ApiArray.New(Emojis.Awesome, Emojis.Party),
            QuotedText = "hello",
            LastEmoji = Emojis.Party,
        };

    private static MessageNotification NewCurrentMessage()
        => MessageNotification.New(TestUserId, TestChatId, 7, Bob) with {
            Title = "Bob",
            Text = "hello",
            SentAt = TestSentAt,
            SenderName = "Bob",
            GroupTitle = "The actual one",
            UnreadCount = 2,
            AuthorIds = ApiArray.New(Bob, Kate),
            RecentMessages = ApiArray.New(new NotificationMessage { Text = "hello" }),
        };

    private static LegacyReactionNotification NewLegacyReaction()
        => new(NotificationId.New(TestUserId, NotificationKind.Reaction, TestEntryId.Value)) {
            Title = "Bob",
            Text = "🥳 to hello",
            SentAt = TestSentAt,
            AuthorId = Bob,
        };

    private static LegacyMessageNotification NewLegacyMessage()
        => new(NotificationId.New(TestUserId, NotificationKind.Message, TestChatId.Value)) {
            Title = "Bob",
            Text = "hello",
            SentAt = TestSentAt,
            AuthorId = Bob,
            EntryLid = 7,
            UnreadCount = 2,
            AuthorIds = ApiArray.New(Bob, Kate),
            RecentMessages = ApiArray.New(new NotificationMessage { Text = "hello" }),
        };

    // Nested types

    [MessagePackObject]
    public sealed record LegacyUserNotificationInfo(
        [property: Key(0)] UserId UserId,
        [property: Key(1)] long Version = 0)
    {
        [Key(2)] public ApiArray<LegacyNotification> Items { get; init; }
        [Key(4)] public Moment LastPushAt { get; init; }
        [Key(5)] public bool IsDormant { get; init; }
        [Key(6)] public ApiArray<PendingDismissal> PendingDismissals { get; init; }
    }

    [MessagePackObject]
    [Union(1, typeof(LegacyMessageNotification))]
    [Union(5, typeof(LegacyReactionNotification))]
    public abstract record LegacyNotification(
        [property: Key(0)] NotificationId Id,
        [property: Key(1)] long Version = 0)
    {
        [Key(2)] public string Title { get; init; } = "";
        [Key(3)] public string Text { get; init; } = "";
        [Key(4)] public string IconUrl { get; init; } = "";
        [Key(5)] public Moment CreatedAt { get; init; }
        [Key(6)] public Moment SentAt { get; init; }
        [Key(8)] public AuthorId? AuthorId { get; init; }
        [Key(16)] public ApiArray<NotificationAction> Actions { get; init; }
    }

    [MessagePackObject]
    [method: SerializationConstructor]
    public sealed record LegacyReactionNotification(NotificationId Id, long Version = 0)
        : LegacyNotification(Id, Version);

    [MessagePackObject]
    [method: SerializationConstructor]
    public sealed record LegacyMessageNotification(NotificationId Id, long Version = 0)
        : LegacyNotification(Id, Version)
    {
        [Key(9)] public long EntryLid { get; init; }
        [Key(10)] public long StartEntryLid { get; init; }
        [Key(11)] public int UnreadCount { get; init; }
        [Key(12)] public ApiArray<AuthorId> AuthorIds { get; init; }
        [Key(13)] public string LeadText { get; init; } = "";
        [Key(14)] public int BeepCount { get; init; }
        [Key(15)] public Moment LastBeepAt { get; init; }
        [Key(17)] public int LeadCount { get; init; }
        [Key(18)] public ApiArray<NotificationMessage> RecentMessages { get; init; }
        [Key(19)] public string BeepGroup { get; init; } = "";
        [Key(20)] public string LastBeepGroup { get; init; } = "";
    }
}
