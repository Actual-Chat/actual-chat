using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

/// <summary>
/// Pins <see cref="ConversationViewState"/> value equality: it is a compute-method argument of
/// ChatUI.GetTile, so it is the tile cache key. Reference equality there re-keys every tile on
/// every rebuild, turning the tile cache into pure garbage.
/// </summary>
public sealed class ConversationViewStateKeyTest
{
    [Fact]
    public void StructurallyIdenticalStatesMustBeEqual()
    {
        // arrange
        var chatId = ChatId.Parse("the-actual-one");
        var expanded = ConversationId.New(chatId, 100);
        IImmutableSet<ConversationId> setA = ImmutableHashSet.Create(expanded);
        IImmutableSet<ConversationId> setB = ImmutableHashSet.Create(expanded);

        // act
        var a = new ConversationViewState(
            true, setA, new Range<long>(1, 5), expanded, new Range<long>(1, 5), null, null);
        var b = new ConversationViewState(
            true, setB, new Range<long>(1, 5), expanded, new Range<long>(1, 5), null, null);

        // assert
        setA.Should().NotBeSameAs(setB, "distinct instances are required or this test is vacuous");
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void NarrowToDropsRangesThatCannotReachTheTile()
    {
        // arrange
        var chatId = ChatId.Parse("the-actual-one");
        var liveBlockId = ConversationId.New(chatId, 9000);
        var state = new ConversationViewState(
            true,
            ImmutableHashSet<ConversationId>.Empty,
            new Range<long>(9100, long.MaxValue),
            liveBlockId,
            new Range<long>(9000, 9100),
            null,
            null);

        // act
        var farTile = state.NarrowTo(new Range<long>(100, 105));
        var nearTile = state.NarrowTo(new Range<long>(9050, 9055));

        // assert
        farTile.HiddenLiveTailRange.Should().Be(default(Range<long>));
        farTile.LiveFoldRange.Should().Be(default(Range<long>));
        // The whole point: tiles far from the live block share one key as the fold boundary advances.
        var advanced = state with { LiveFoldRange = new Range<long>(9000, 9200) };
        advanced.NarrowTo(new Range<long>(100, 105)).Should().Be(farTile);
        nearTile.LiveFoldRange.Should().Be(new Range<long>(9000, 9100), "an overlapping range must survive");
    }
}
