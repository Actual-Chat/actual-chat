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

    // Private methods

    private static ReactionNotification NewReaction(AuthorId authorId, Emoji emoji, Moment sentAt)
        => ReactionNotification.New(TestUserId, TestEntryId, authorId) with {
            AuthorIds = ApiArray.New(authorId),
            Emojis = ApiArray.New(emoji),
            SentAt = sentAt,
        };
}
