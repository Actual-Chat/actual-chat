using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Services;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class NotificationsUIProjectionTest
{
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");

    [Fact]
    public void ListByKindShouldFilterAndSortNewestFirst()
    {
        // arrange
        var older = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var newer = NewReaction(2, Moment.EpochStart + TimeSpan.FromSeconds(2));
        var mention = MentionNotification.New(TestUserId, ChatEntryId.New(ChatA, 3));
        var active = ApiArray.New<Notification>(older, mention, newer);

        // act
        var result = NotificationsUI.SelectByKind(active, NotificationKind.Reaction);

        // assert
        result.Select(x => x.Id).Should().Equal([newer.Id, older.Id]);
    }

    [Fact]
    public void ReactionStateShouldTakeNewestReactionForChat()
    {
        // arrange
        var older = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(1)) with {
            Emojis = ApiArray.New(Emojis.Awesome),
        };
        var newer = NewReaction(2, Moment.EpochStart + TimeSpan.FromSeconds(2)) with {
            Emojis = ApiArray.New(Emojis.Party),
        };
        var active = ApiArray.New<Notification>(older, newer);

        // act
        var state = NotificationsUI.SelectReactionState(active, ChatA);

        // assert
        state.Emoji.Should().Be(Emojis.Party);
        state.SentAt.Should().Be(newer.SentAt);
    }

    [Fact]
    public void ReactionStateShouldPreferLastEmojiOverAccumulatedOrder()
    {
        // arrange - an out-of-order merge leaves the newest emoji in the middle of the accumulated set
        var notification = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(2)) with {
            Emojis = ApiArray.New(Emojis.Party, Emojis.Awesome),
            LastEmoji = Emojis.Party,
        };
        var active = ApiArray.New<Notification>(notification);

        // act
        var state = NotificationsUI.SelectReactionState(active, ChatA);

        // assert
        state.Emoji.Should().Be(Emojis.Party);
    }

    [Fact]
    public void ReactionStateShouldFallBackToAccumulatedSetWithoutLastEmoji()
    {
        // arrange - a notification persisted before LastEmoji existed
        var notification = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(1)) with {
            Emojis = ApiArray.New(Emojis.Awesome, Emojis.Party),
        };
        var active = ApiArray.New<Notification>(notification);

        // act
        var state = NotificationsUI.SelectReactionState(active, ChatA);

        // assert
        state.Emoji.Should().Be(Emojis.Party);
    }

    [Fact]
    public void ReactionStateShouldBeDefaultForChatWithoutReactions()
    {
        // act
        var state = NotificationsUI.SelectReactionState(ApiArray<Notification>.Empty, ChatA);

        // assert
        state.Should().Be(default(ChatReactionState));
    }

    // Private methods

    private static ReactionNotification NewReaction(long entryLid, Moment sentAt)
        => ReactionNotification.New(TestUserId, ChatEntryId.New(ChatA, entryLid)) with { SentAt = sentAt };
}
