namespace ActualChat.Notifications.IntegrationTests;

public sealed class ReactionNotificationMergeTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly ChatEntryId TestEntryId = ChatEntryId.New(TestChatId, 1);

    [Fact]
    public void MergeShouldAccumulateDistinctReactors()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var first = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var second = NewReaction(kate, Emojis.Party, Moment.EpochStart + TimeSpan.FromSeconds(2));

        // act
        var merged = (ReactionNotification)second.MergeWith(first);

        // assert
        merged.AuthorIds.Should().Equal([bob, kate]);
        merged.Emojis.Should().Equal([Emojis.Awesome, Emojis.Party]);
    }

    [Fact]
    public void MergeShouldKeepNewestDisplayFields()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var first = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1))
            with { Title = "Bob", Text = "reacted" };
        var second = NewReaction(kate, Emojis.Party, Moment.EpochStart + TimeSpan.FromSeconds(2))
            with { Title = "Kate", Text = "reacted too" };

        // act
        var merged = (ReactionNotification)second.MergeWith(first);

        // assert
        merged.Title.Should().Be("Kate", because: "old clients read Title and must see the latest reaction");
        merged.AuthorId.Should().Be(kate);
    }

    [Fact]
    public void MergeShouldNotLetOlderEventRegressDisplayFields()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var newer = NewReaction(kate, Emojis.Party, Moment.EpochStart + TimeSpan.FromSeconds(2))
            with { Title = "Kate" };
        var older = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1))
            with { Title = "Bob" };

        // act
        var merged = (ReactionNotification)older.MergeWith(newer);

        // assert
        merged.Title.Should().Be("Kate");
        merged.SentAt.Should().Be(newer.SentAt);
        merged.AuthorIds.Should().Contain(bob);
    }

    [Fact]
    public void MergeShouldReturnExistingInstanceOnRedelivery()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var first = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var merged = first.MergeWith(null);
        var redelivered = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(2));

        // act
        var result = redelivered.MergeWith(merged);

        // assert
        result.Should().BeSameAs(merged, because: "an unchanged merge must suppress the duplicate push");
    }

    [Fact]
    public void MergeShouldFreezeAtAuthorCap()
    {
        // arrange
        Notification accumulated = NewReaction(AuthorId.New(TestChatId, 100), Emojis.Awesome, Moment.EpochStart);
        for (var i = 1; i <= Constants.Notification.MaxReactionAuthors; i++) {
            var next = NewReaction(
                AuthorId.New(TestChatId, 100 + i),
                Emojis.Party,
                Moment.EpochStart + TimeSpan.FromSeconds(i));
            accumulated = next.MergeWith(accumulated);
        }
        var atCap = (ReactionNotification)accumulated;

        // act
        var extra = NewReaction(
            AuthorId.New(TestChatId, 999),
            Emojis.Awesome,
            Moment.EpochStart + TimeSpan.FromMinutes(1));
        var result = extra.MergeWith(atCap);

        // assert
        atCap.AuthorIds.Count.Should().Be(Constants.Notification.MaxReactionAuthors);
        result.Should().BeSameAs(atCap, because: "past the cap the notification stops changing and stops re-pushing");
    }

    [Fact]
    public void MergeShouldKeepLastEmojiOfNewestEvent()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var newer = NewReaction(kate, Emojis.Party, Moment.EpochStart + TimeSpan.FromSeconds(2));
        var older = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1));

        // act
        var merged = (ReactionNotification)older.MergeWith(newer);

        // assert
        merged.Emojis.Should().Equal([Emojis.Party, Emojis.Awesome], because: "emojis accumulate in arrival order");
        merged.LastEmoji.Should().Be(Emojis.Party, because: "an out-of-order older event must not steal the badge");
    }

    [Fact]
    public void MergeShouldUpdateLastEmojiWhenNewestReusesAnAccumulatedEmoji()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var mark = AuthorId.New(TestChatId, 7);
        var accumulated = NewReaction(kate, Emojis.Party, Moment.EpochStart + TimeSpan.FromSeconds(2))
            .MergeWith(NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1)));

        // act
        var third = NewReaction(mark, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(3));
        var merged = (ReactionNotification)third.MergeWith(accumulated);

        // assert
        merged.Emojis.Should().Equal([Emojis.Awesome, Emojis.Party], because: "the reused emoji is deduplicated");
        merged.LastEmoji.Should().Be(Emojis.Awesome);
    }

    [Fact]
    public void MergeShouldUpdateBadgeWhenAuthorReReactsWithAccumulatedEmoji()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var accumulated = NewReaction(kate, Emojis.Party, Moment.EpochStart + TimeSpan.FromSeconds(2))
            .MergeWith(NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1)));

        // act - bob re-reacts with an accumulated emoji that is not the current badge
        var reReaction = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(3));
        var merged = (ReactionNotification)reReaction.MergeWith(accumulated);

        // assert
        merged.Should().NotBeSameAs(accumulated, because: "both sets are unchanged, but the badge and SentAt are not");
        merged.LastEmoji.Should().Be(Emojis.Awesome);
        merged.SentAt.Should().Be(reReaction.SentAt);
    }

    // Private methods

    private static ReactionNotification NewReaction(AuthorId authorId, Emoji emoji, Moment sentAt)
        // Mirrors the send site: Emojis and LastEmoji are filled together.
        => ReactionNotification.New(TestUserId, TestEntryId, authorId) with {
            AuthorIds = ApiArray.New(authorId),
            Emojis = ApiArray.New(emoji),
            LastEmoji = emoji,
            SentAt = sentAt,
        };
}
