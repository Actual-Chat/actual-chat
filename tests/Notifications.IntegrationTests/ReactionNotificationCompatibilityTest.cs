using ActualChat.Notifications.Db;

namespace ActualChat.Notifications.IntegrationTests;

// v2.18 added keys 9..12 to ReactionNotification. The record is array-form MessagePack and the
// base chain already declared key 16, so every row v2.17 wrote carries nil in those slots, and
// the plain ApiArray formatter throws on nil. The Legacy* records below mirror the v2.17 layout
// so the tests can produce exactly the bytes v2.17 pods and clients produce.
public sealed class ReactionNotificationCompatibilityTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly ChatEntryId TestEntryId = ChatEntryId.New(TestChatId, 1);
    private static readonly AuthorId Bob = AuthorId.New(TestChatId, 5);
    private static readonly AuthorId Kate = AuthorId.New(TestChatId, 6);
    private static readonly Moment TestSentAt = Moment.EpochStart + TimeSpan.FromSeconds(1);
    private static readonly MessagePackSerializerOptions Options = MessagePackByteSerializer.DefaultOptions;

    [Fact]
    public void ServerShouldReadRowWrittenByPreviousRelease()
    {
        // arrange
        var legacy = new LegacyUserNotificationInfo(TestUserId, 1) {
            Items = ApiArray.New<LegacyNotification>(NewLegacyReaction()),
        };
        var row = new DbUserNotifications {
            Id = TestUserId.Value,
            Version = 1,
            ItemsData = [0, ..MessagePackSerializer.Serialize(legacy, Options)],
        };

        // act
        var info = row.ToModel();

        // assert
        var reaction = info.Items.Should().ContainSingle().Which.Should().BeOfType<ReactionNotification>().Which;
        reaction.Id.Should().Be(legacy.Items[0].Id);
        reaction.Title.Should().Be("Bob");
        reaction.AuthorId.Should().Be(Bob);
        reaction.SentAt.Should().Be(TestSentAt);
        reaction.AuthorIds.IsEmpty.Should().BeTrue();
        reaction.Emojis.IsEmpty.Should().BeTrue();
        reaction.QuotedText.Should().Be("");
        reaction.LastEmoji.Should().BeNull();
    }

    [Fact]
    public void PreviousReleaseClientShouldReadCurrentServerOutput()
    {
        // arrange
        var info = new UserNotificationInfo(TestUserId, 1) {
            Items = ApiArray.New<Notification>(NewCurrentReaction()),
        };

        // act
        var bytes = MessagePackSerializer.Serialize(info, Options);
        var legacy = MessagePackSerializer.Deserialize<LegacyUserNotificationInfo>(bytes, Options);

        // assert
        var item = legacy.Items.Should().ContainSingle().Which;
        var reaction = item.Should().BeOfType<LegacyReactionNotification>().Which;
        reaction.Id.Should().Be(info.Items[0].Id);
        reaction.Title.Should().Be("Bob");
        reaction.AuthorId.Should().Be(Bob);
        reaction.SentAt.Should().Be(TestSentAt);
    }

    [Fact]
    public void CurrentRowShouldRoundTripReactors()
    {
        // arrange
        var info = new UserNotificationInfo(TestUserId, 1) {
            Items = ApiArray.New<Notification>(NewCurrentReaction()),
        };
        var row = new DbUserNotifications();

        // act
        row.UpdateFrom(info);
        var reaction = (ReactionNotification)row.ToModel().Items[0];

        // assert
        reaction.AuthorIds.Should().Equal([Bob, Kate]);
        reaction.Emojis.Should().Equal([Emojis.Awesome, Emojis.Party]);
        reaction.QuotedText.Should().Be("hello");
        reaction.LastEmoji.Should().Be(Emojis.Party);
    }

    // Private methods

    private static ReactionNotification NewCurrentReaction()
        => ReactionNotification.New(TestUserId, TestEntryId, Bob) with {
            Title = "Bob",
            Text = "🥳 to hello",
            SentAt = TestSentAt,
            AuthorIds = ApiArray.New(Bob, Kate),
            Emojis = ApiArray.New(Emojis.Awesome, Emojis.Party),
            QuotedText = "hello",
            LastEmoji = Emojis.Party,
        };

    private static LegacyReactionNotification NewLegacyReaction()
        => new(NotificationId.New(TestUserId, NotificationKind.Reaction, TestEntryId.Value)) {
            Title = "Bob",
            Text = "🥳 to hello",
            SentAt = TestSentAt,
            AuthorId = Bob,
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
}
