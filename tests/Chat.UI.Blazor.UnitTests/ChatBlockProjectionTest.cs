using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ChatBlockProjectionTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void CompletedBlockShouldUseMetadataCoverageAndCollapseAfterItsMessages()
    {
        // arrange
        var conversation = NewConversation(100, 119);
        var expanded = ImmutableHashSet.Create(conversation.Id);
        var view = NewView() with { ExpandedConversations = expanded };

        // act
        var blocks = ChatUI.BuildChatBlocks(TestChatId, [new(100, 200)], [conversation], view, null, default);

        // assert
        var block = blocks.Should().ContainSingle().Subject;
        block.EntryLidRange.Should().Be(new Range<long>(100, 200));
        block.IsExpanded.Should().BeTrue();
        block.IsLive.Should().BeFalse();
        block.CollapsedAt.Should().BeGreaterThan(conversation.EndsAt);
    }

    [Fact]
    public void LiveBlockShouldOwnItsFullTailWithoutAbsorbingAnOverlappingConversation()
    {
        // arrange
        var live = NewConversation(100, 119);
        var overlapping = NewConversation(90, 150);
        var preceding = NewConversation(50, 80);
        var view = NewView() with { LiveBlockConversationId = live.Id };

        // act
        var blocks = ChatUI.BuildChatBlocks(TestChatId,
            [preceding.EntryLidRange, overlapping.EntryLidRange, live.EntryLidRange],
            [preceding, overlapping, live], view, live, new(100, 250));

        // assert
        blocks.Select(b => b.Id).Should().Equal(preceding.Id, overlapping.Id, live.Id);
        blocks[1].EntryLidRange.Should().Be(new Range<long>(90, 100));
        blocks[2].EntryLidRange.Should().Be(new Range<long>(100, 250));
        blocks[2].IsLive.Should().BeTrue();
        blocks[2].IsExpanded.Should().BeFalse();
        blocks[2].CollapsedAt.Should().BeLessThanOrEqualTo(live.StartsAt);
        var meta = new ChatRangeTile(new(0, 1280), [new(50, 250)],
            [preceding.EntryLidRange, overlapping.EntryLidRange, live.EntryLidRange], 0, null, null);
        ChatUI.TryGetIdTilesToLoad(view, blocks,
            new(new(230, 239), 0, 0), [meta], out var tiles, out _, out _).Should().BeTrue();
        tiles.Should().Contain(new Range<long>(100, 105));
    }

    [Fact]
    public void MaterializedBlockShouldKeepItsRenderIdentityAndExpansion()
    {
        // arrange
        var materialized = NewConversation(90, 249);
        var renderId = ConversationId.New(TestChatId, 100);
        var view = NewView() with {
            LiveBlockConversationId = renderId,
            MaterializedBlockId = materialized.Id,
            ExpandedConversations = ImmutableHashSet.Create(renderId),
        };

        // act
        var blocks = ChatUI.BuildChatBlocks(TestChatId, [materialized.EntryLidRange],
            [materialized], view, null, new(100, 250));

        // assert
        var block = blocks.Should().ContainSingle().Subject;
        block.Id.Should().Be(renderId);
        block.IsLive.Should().BeFalse();
        block.IsExpanded.Should().BeTrue();
        block.CollapsedAt.Should().BeGreaterThan(materialized.EndsAt);
        block.EntryLidRange.Should().Be(new Range<long>(100, 250));
    }

    [Fact]
    public void MissingConversationShouldNotInventABlock()
    {
        // act
        var blocks = ChatUI.BuildChatBlocks(TestChatId, [new(100, 200)], [], NewView(), null, default);

        // assert
        blocks.Should().BeEmpty();
    }

    [Fact]
    public void MissingLiveRecordShouldStillReserveTheOpenEndedTail()
    {
        var earlier = NewConversation(50, 199);
        var later = NewConversation(150, 249);
        var view = NewView() with { LiveBlockConversationId = ConversationId.New(TestChatId, 100) };

        var blocks = ChatUI.BuildChatBlocks(TestChatId, [earlier.EntryLidRange, later.EntryLidRange],
            [earlier, later], view, null, new(100, 250));

        blocks.Should().ContainSingle().Which.EntryLidRange.Should().Be(new Range<long>(50, 100));
    }

    [Fact]
    public void WitnessedLiveRowsShouldNotExpandTheOlderConversationPrefix()
    {
        // arrange
        var witnessed = new LidRangeSet();
        witnessed.Add(new(150, 160));
        var empty = ImmutableHashSet<ConversationId>.Empty;

        // act
        var result = ChatUI.GetNewAutoExpansions(TestChatId, [new(50, 200)], empty, empty, empty,
            _ => false, witnessed, ConversationId.New(TestChatId, 100), null);

        // assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void MaterializedAliasShouldReplaceAStaleLiveRecordWithTheSameRenderId()
    {
        // arrange
        var live = NewConversation(100, 149) with { Title = "Live" };
        var materialized = NewConversation(90, 199) with { Title = "Completed" };
        var view = NewView() with { LiveBlockConversationId = live.Id, MaterializedBlockId = materialized.Id };
        var meta = ConversationRangeTile.NewNormalized(TestChatId, new(100, 200), [new(100, 200)]);

        // act
        var records = ChatUI.ResolveBlockAliases([live, materialized], view);
        var tile = meta.ApplyTo(records, new(100, 200));

        // assert
        var conversation = tile.Should().ContainSingle().Subject;
        conversation.Id.Should().Be(live.Id);
        conversation.Title.Should().Be("Completed");
    }

    [Fact]
    public void ClippedMaterializedPrefixShouldKeepTheLiveAliasesCoverage()
    {
        // arrange
        var live = NewConversation(100, 249) with { Title = "Live" };
        var materialized = NewConversation(90, 99) with { Title = "Completed" };
        var view = NewView() with { LiveBlockConversationId = live.Id, MaterializedBlockId = materialized.Id };

        // act
        var records = ChatUI.ResolveBlockAliases([materialized, live], view);
        var meta = ConversationRangeTile.NewNormalized(TestChatId, new(100, 250),
            records.Select(c => c.EntryLidRange));
        var tile = meta.ApplyTo(records, new(100, 250));

        // assert
        var conversation = tile.Should().ContainSingle().Subject;
        conversation.EntryLidRange.Should().Be(new Range<long>(100, 250));
        conversation.Title.Should().Be("Completed");
    }

    [Fact]
    public void NestedLaterConversationShouldTruncateTheEarlierBlockWithoutResumingIt()
    {
        // arrange
        var earlier = NewConversation(50, 499);
        var later = NewConversation(100, 119);

        // act
        var blocks = ChatUI.BuildChatBlocks(TestChatId, [earlier.EntryLidRange, later.EntryLidRange],
            [earlier, later], NewView(), null, default);

        // assert
        blocks.Select(b => b.EntryLidRange).Should().Equal(new Range<long>(50, 100), new Range<long>(100, 120));
        blocks[0].Conversation.EndEntryLid.Should().Be(99);
    }

    [Fact]
    public void ActiveLiveBlockShouldOwnTheTailPastLaterConversations()
    {
        // arrange
        var live = NewConversation(100, 199);
        var later = NewConversation(150, 249);
        var view = NewView() with {
            LiveBlockConversationId = live.Id,
            LiveFoldRange = new(100, 200),
            HiddenLiveTailRange = new(100, long.MaxValue),
        };
        var blocks = ChatUI.BuildChatBlocks(TestChatId, [live.EntryLidRange, later.EntryLidRange],
            [live, later], view, live, new(100, 250));
        var meta = new ChatRangeTile(new(0, 1280), [new(100, 250)],
            [live.EntryLidRange, later.EntryLidRange], 0, null, null);

        // act
        var isFulfilled = ChatUI.TryGetIdTilesToLoad(view, blocks,
            new(new(170, 179), 0, 0), [meta], out var tiles, out _, out _);

        // assert
        isFulfilled.Should().BeTrue();
        blocks.Should().ContainSingle().Which.EntryLidRange.Should().Be(new Range<long>(100, 250));
        tiles.Should().Contain(new Range<long>(100, 105));
        tiles.Should().NotContain(new Range<long>(150, 155));
    }

    [Fact]
    public void LaterBlockShouldNotTruncateActiveLiveTranscriptFiltering()
    {
        // arrange
        var view = NewView() with {
            LiveBlockConversationId = ConversationId.New(TestChatId, 100),
            LiveFoldRange = new(100, 200),
            HiddenLiveTailRange = new(120, long.MaxValue),
        };

        // act
        var result = ChatUI.TruncateMaterializedRanges(view, [new(150, 170)]);

        // assert
        result.LiveFoldRange.Should().Be(view.LiveFoldRange);
        result.HiddenLiveTailRange.Should().Be(view.HiddenLiveTailRange);
        result.HiddenLiveTailRange.Contains(180).Should().BeTrue();
    }

    [Fact]
    public void MaterializedBlockShouldStillYieldToALaterFiniteConversation()
    {
        var materialized = NewConversation(100, 199);
        var later = NewConversation(150, 249);
        var view = NewView() with {
            LiveBlockConversationId = materialized.Id,
            MaterializedBlockId = materialized.Id,
            LiveFoldRange = new(100, 200),
            HiddenLiveTailRange = new(120, 200),
        };

        var blocks = ChatUI.BuildChatBlocks(TestChatId, [materialized.EntryLidRange, later.EntryLidRange],
            [materialized, later], view, null, new(100, 200));
        var filtered = ChatUI.TruncateMaterializedRanges(view, [later.EntryLidRange]);

        blocks.Select(b => b.EntryLidRange).Should().Equal(new Range<long>(100, 150), new Range<long>(150, 250));
        filtered.LiveFoldRange.Should().Be(new Range<long>(100, 150));
        filtered.HiddenLiveTailRange.Should().Be(new Range<long>(120, 150));
    }

    // Private methods

    private static ConversationViewState NewView()
        => new(true, ImmutableHashSet<ConversationId>.Empty, default, null, default, null);

    private static Conversation NewConversation(long start, long end)
        => new(ConversationId.New(TestChatId, start)) {
            EndEntryLid = end,
            StartsAt = Moment.EpochStart + TimeSpan.FromSeconds(start),
            EndsAt = Moment.EpochStart + TimeSpan.FromSeconds(end),
        };
}
