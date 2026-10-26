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
        var model = ReactionsOverlay.Fold([], null, NewCommand(ThumbsUp, mustRemove: false));

        // assert
        model.OwnReaction!.Emoji.Should().Be(ThumbsUp);
        model.Summaries.Single().Count.Should().Be(1);
    }

    [Fact]
    public void PendingEmojiChangeShouldReplaceOwnReaction()
    {
        // arrange
        var own = NewReaction(ThumbsUp);
        var summaries = new[] { NewSummary(ThumbsUp, 1) };

        // act
        var model = ReactionsOverlay.Fold(summaries, own, NewCommand(Heart, mustRemove: false));

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
        var model = ReactionsOverlay.Fold(summaries, own, NewCommand(ThumbsUp, mustRemove: true));

        // assert
        model.OwnReaction.Should().BeNull();
        model.Summaries.Single().Count.Should().Be(2, because: "only the own reaction is taken away");
    }

    [Fact]
    public void RemovingTheOnlyReactionShouldLeaveNothingToShow()
    {
        // arrange
        var own = NewReaction(ThumbsUp);
        var summaries = new[] { NewSummary(ThumbsUp, 1) };

        // act
        var model = ReactionsOverlay.Fold(summaries, own, NewCommand(ThumbsUp, mustRemove: true));

        // assert
        model.IsEmpty.Should().BeTrue(because: "an empty row would still render its add button");
    }

    [Fact]
    public void PendingCommandAlreadyOnServerShouldChangeNothing()
    {
        // arrange
        var own = NewReaction(ThumbsUp);
        var summaries = new[] { NewSummary(ThumbsUp, 1) };

        // act
        var model = ReactionsOverlay.Fold(summaries, own, NewCommand(ThumbsUp, mustRemove: false));

        // assert
        model.OwnReaction.Should().BeSameAs(own);
        model.Summaries.Should().BeEquivalentTo(summaries,
            because: "a command that only restates the server state must not count the reaction twice");
    }

    [Fact]
    public void IsReflectedShouldFollowTheCommandIntent()
    {
        // arrange
        var own = NewReaction(ThumbsUp);

        // act & assert
        ReactionsOverlay.IsReflected(own, NewCommand(ThumbsUp, mustRemove: false)).Should().BeTrue();
        ReactionsOverlay.IsReflected(own, NewCommand(Heart, mustRemove: false)).Should().BeFalse();
        ReactionsOverlay.IsReflected(null, NewCommand(ThumbsUp, mustRemove: false)).Should().BeFalse();
        ReactionsOverlay.IsReflected(own, NewCommand(ThumbsUp, mustRemove: true)).Should().BeFalse();
        ReactionsOverlay.IsReflected(null, NewCommand(ThumbsUp, mustRemove: true)).Should().BeTrue(
            because: "a removal is reflected once the server no longer shows that emoji");
    }

    // Private methods

    private static Reactions_React NewCommand(Emoji emoji, bool mustRemove)
        => new() { Session = Session.New(), Reaction = NewReaction(emoji), MustRemove = mustRemove };

    private static Reaction NewReaction(Emoji emoji)
        => new() { Id = Symbol.Empty, AuthorId = default!, EntryId = EntryId, Emoji = emoji };

    private static ReactionSummary NewSummary(Emoji emoji, long count)
        => new() { Id = Symbol.Empty, EntryId = EntryId, Emoji = emoji, Count = count };
}
