using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ChatBlockQueryTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Theory]
    [InlineData(130, 160)]
    [InlineData(130, 250)]
    [InlineData(50, 160)]
    public void CollapsedConversationShouldKeepItsCardAtEitherBoundary(long start, long end)
    {
        // arrange
        var blockRange = new Range<long>(100, 200);
        var meta = NewMeta([new(50, 300)], [blockRange]);
        var query = new ChatDataQuery(new(start, end), 0, 0);

        // act
        var tiles = Select(query, meta, NewView(), [blockRange]);

        // assert
        tiles.Should().Contain(ChatUI.EntryIdTiles.GetTile(100L).Range);
    }

    [Fact]
    public void WindowInsideTwoCollapsedConversationsShouldKeepBothCards()
    {
        // arrange
        var blocks = new Range<long>[] { new(100, 200), new(300, 400) };
        var meta = NewMeta([new(100, 400)], blocks);

        // act
        var tiles = Select(new(new(130, 330), 0, 0), meta, NewView(), blocks);

        // assert
        tiles.Should().Contain(ChatUI.EntryIdTiles.GetTile(100L).Range);
        tiles.Should().Contain(ChatUI.EntryIdTiles.GetTile(300L).Range);
        tiles.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(50, 99)]
    [InlineData(200, 240)]
    public void AdjacentWindowShouldNotPullInCollapsedConversation(long start, long end)
    {
        // arrange
        var block = new Range<long>(100, 200);
        var meta = NewMeta([new(50, 300)], [block]);

        // act
        var tiles = Select(new(new(start, end), 0, 0), meta, NewView(), [block]);

        // assert
        tiles.Should().NotContain(ChatUI.EntryIdTiles.GetTile(100L).Range);
    }

    [Fact]
    public void ExpandedConversationShouldKeepItsEntriesVirtualized()
    {
        // arrange
        var block = new Range<long>(100, 200);
        var meta = NewMeta([block], [block]);
        var view = NewView() with {
            ExpandedConversations = ImmutableHashSet.Create(ConversationId.New(TestChatId, 100)),
        };

        // act
        var tiles = Select(new(new(130, 139), 0, 0), meta, view, []);

        // assert
        tiles.Should().Equal(new Range<long>(130, 135), new Range<long>(135, 140));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpandedLiveBlockShouldFoldUntilItMaterializes(bool isMaterialized)
    {
        // arrange
        var id = ConversationId.New(TestChatId, 100);
        var view = NewView() with {
            LiveBlockConversationId = id,
            MaterializedBlockId = isMaterialized ? id : null,
            ExpandedConversations = ImmutableHashSet.Create(id),
            LiveFoldRange = new(100, 120),
        };
        var range = new Range<long>(100, 200);

        // act
        var tiles = Select(new(new(100, 140), 0, 0), NewMeta([range], [range]), view, [range]);

        // assert
        tiles.Contains(new(110, 115)).Should().Be(isMaterialized);
        tiles.Should().Contain(new Range<long>(120, 125));
    }

    [Fact]
    public void CollapsedLiveTailShouldIncludeItsCardBeyondPersistedCoverage()
    {
        // arrange
        var liveId = ConversationId.New(TestChatId, 100);
        var view = NewView() with {
            LiveBlockConversationId = liveId,
            LiveFoldRange = new(100, 120),
            HiddenLiveTailRange = new(120, long.MaxValue),
        };
        var meta = NewMeta([new(100, 250)], [new(100, 120)]);

        // act
        var tiles = Select(new(new(230, 239), 0, 0), meta, view, [new(100, 250)]);

        // assert
        tiles.Should().Contain(ChatUI.EntryIdTiles.GetTile(100L).Range);
        tiles.Should().Contain(ChatUI.EntryIdTiles.GetTile(245L).Range);
        tiles.Should().HaveCount(27, "whole live coverage loads the card plus 26 tiles after the fold");
        tiles.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CollapsedOnlyWindowShouldLoadOneCardTileAndKnowBothEdges()
    {
        // arrange
        var block = new Range<long>(100, 200);

        // act
        var isFulfilled = ChatUI.TryGetIdTilesToLoad(NewView(), NewBlocks(NewView(), [block]),
            new(new(130, 160), 0, 0), [NewMeta([block], [block])],
            out var tiles, out var hasBefore, out var hasAfter);

        // assert
        isFulfilled.Should().BeTrue();
        tiles.Should().Equal(new Range<long>(100, 105));
        hasBefore.Should().BeFalse();
        hasAfter.Should().BeFalse();
    }

    [Fact]
    public void OffsetsPastCollapsedBlockShouldAllowItsCardToUnload()
    {
        // arrange
        var block = new Range<long>(100, 200);
        var meta = NewMeta([new(100, 300)], [block]);

        // act
        var tiles = Select(new(new(130, 160), 1, 1), meta, NewView(), [block]);

        // assert
        tiles.Should().Equal(new Range<long>(200, 205));
    }

    [Fact]
    public void VisibleCoverageInsideCollapsedBlockShouldKeepItsCard()
    {
        // arrange
        var block = new Range<long>(100, 200);
        var meta = NewMeta([new(100, 300)], [block]);
        var query = new ChatDataQuery(new(210, 220), 0, 0) { VisibleLidRange = new(130, 131) };

        // act
        var tiles = Select(query, meta, NewView(), [block]);

        // assert
        tiles.Should().Contain(new Range<long>(100, 105));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExpandedCoverageShouldRequestMissingRangeTiles(bool isBefore)
    {
        // arrange
        var block = new Range<long>(100, 3000);
        var meta = new ChatRangeTile(new(1280, 2560), [new(1280, 2560)], [block], 1,
            isBefore ? 0 : null, isBefore ? null : 2560);

        // act
        var isFulfilled = ChatUI.TryGetIdTilesToLoad(NewView(), NewBlocks(NewView(), [block]),
            new(new(1400, 1500), 0, 0), [meta], out _, out _, out _);

        // assert
        isFulfilled.Should().BeFalse("normalization reaches metadata not loaded by the original query");
    }

    [Fact]
    public void DisabledConversationsShouldKeepOrdinarySelection()
    {
        // arrange
        var block = new Range<long>(100, 200);
        var view = NewView() with { ShowConversations = false };

        // act
        var tiles = Select(new(new(130, 139), 0, 0), NewMeta([block], [block]), view, [block]);

        // assert
        tiles.Should().Equal(new Range<long>(130, 135), new Range<long>(135, 140));
    }

    // Private methods

    private static ConversationViewState NewView()
        => new(true, ImmutableHashSet<ConversationId>.Empty, default, null, default, null);

    private static ChatRangeTile NewMeta(Range<long>[] entries, Range<long>[] conversations)
        => new(new(0, 1280), entries, conversations, 0, null, null);

    private static List<Range<long>> Select(
        ChatDataQuery query,
        ChatRangeTile meta,
        ConversationViewState view,
        IReadOnlyList<Range<long>> collapsedBlocks)
    {
        var ranges = meta.ConversationRanges.Concat(collapsedBlocks)
            .GroupBy(r => r.Start).Select(g => new Range<long>(g.Key, g.Max(r => r.End))).ToArray();
        ChatUI.TryGetIdTilesToLoad(view, NewBlocks(view, ranges), query, [meta],
            out var tiles, out _, out _).Should().BeTrue();
        return tiles;
    }

    private static ChatBlock[] NewBlocks(ConversationViewState view, IReadOnlyList<Range<long>> ranges)
        => ranges.Select(r => {
            var id = ConversationId.New(TestChatId, r.Start);
            return new ChatBlock(new(id) { EndEntryLid = r.End - 1 }, r,
                view.ExpandedConversations.Contains(id),
                id == view.LiveBlockConversationId && view.MaterializedBlockId == null);
        }).ToArray();
}
