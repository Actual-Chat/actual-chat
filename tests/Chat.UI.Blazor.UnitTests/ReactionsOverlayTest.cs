using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ReactionsOverlayTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatEntryId EntryId = ChatEntryId.New(ChatId.Parse("aaaaaaaaaaaaaaaaaaaa"), 1);
    private static readonly Emoji ThumbsUp = Emojis.ThumbsUp;
    private static readonly Emoji Heart = Emojis.Love;

    [Fact]
    public void PendingAddShouldAppearBeforeServerConfirms()
    {
        // act
        var model = ReactionsOverlay.Fold([], null, [ThumbsUp], EntryId);

        // assert
        model.OwnReaction!.Emoji.Should().Be(ThumbsUp);
        model.Summaries.Single().Count.Should().Be(1);
    }

    [Fact]
    public void TwoPendingClicksOnSameEmojiShouldCancelOut()
    {
        // act
        var model = ReactionsOverlay.Fold([], null, [ThumbsUp, ThumbsUp], EntryId);

        // assert
        model.OwnReaction.Should().BeNull(because: "React is a toggle, so an even number of clicks is a no-op");
        model.Summaries.Should().BeEmpty();
    }

    [Fact]
    public void PendingEmojiChangeShouldReplaceOwnReaction()
    {
        // arrange
        var own = NewReaction(ThumbsUp);
        var summaries = new[] { NewSummary(ThumbsUp, 1) };

        // act
        var model = ReactionsOverlay.Fold(summaries, own, [Heart], EntryId);

        // assert
        model.OwnReaction!.Emoji.Should().Be(Heart);
        model.Summaries.Select(x => x.Emoji).Should().Equal([Heart],
            because: "the previous emoji's summary drops to zero and disappears");
    }

    [Fact]
    public void OtherAuthorsCountsShouldSurviveOwnRemoval()
    {
        // arrange
        var own = NewReaction(ThumbsUp);
        var summaries = new[] { NewSummary(ThumbsUp, 3) };

        // act
        var model = ReactionsOverlay.Fold(summaries, own, [ThumbsUp], EntryId);

        // assert
        model.OwnReaction.Should().BeNull();
        model.Summaries.Single().Count.Should().Be(2, because: "only the own reaction is taken away");
    }

    [Fact]
    public void EmptyPendingListShouldReturnServerStateUnchanged()
    {
        // arrange
        var own = NewReaction(ThumbsUp);
        var summaries = new[] { NewSummary(ThumbsUp, 1) };

        // act
        var model = ReactionsOverlay.Fold(summaries, own, [], EntryId);

        // assert
        model.OwnReaction.Should().BeSameAs(own);
        model.Summaries.Should().BeEquivalentTo(summaries);
    }

    [Fact]
    public void IsReflectedShouldBeTrueWhenServerShowsThePendingEmoji()
    {
        // arrange
        var own = NewReaction(ThumbsUp);

        // act & assert
        ReactionsOverlay.IsReflected(own, ThumbsUp).Should().BeTrue();
        ReactionsOverlay.IsReflected(own, Heart).Should().BeFalse();
        ReactionsOverlay.IsReflected(null, ThumbsUp).Should().BeFalse();
    }

    // Private methods

    private static Reaction NewReaction(Emoji emoji)
        => new() { Id = Symbol.Empty, AuthorId = default!, EntryId = EntryId, Emoji = emoji };

    private static ReactionSummary NewSummary(Emoji emoji, long count)
        => new() { Id = Symbol.Empty, EntryId = EntryId, Emoji = emoji, Count = count };
}
