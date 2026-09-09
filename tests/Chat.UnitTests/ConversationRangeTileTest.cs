namespace ActualChat.Chat.UnitTests;

public sealed class ConversationRangeTileTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void OpenEndedRangeShouldOwnTheTailWithoutChangingItsBoundaries()
    {
        var meta = ConversationRangeTile.NewNormalized(TestChatId, new(200, 220),
            [new(50, 300), new(100, long.MaxValue), new(200, 250)]);

        meta.ConversationRanges.Should().Equal(new Range<long>(100, long.MaxValue));
        meta.PreviousConversationRange.Should().Be(new Range<long>(50, 100));
        meta.NextConversationRange.Should().BeNull();
    }

    [Fact]
    public void OpenEndedMetadataShouldKeepTheActualConversationEnd()
    {
        var conversation = new Conversation(ConversationId.New(TestChatId, 100)) { EndEntryLid = 149 };
        var meta = ConversationRangeTile.NewNormalized(TestChatId, new(100, 200), [new(100, long.MaxValue)]);

        var tile = meta.ApplyTo([conversation], new(100, 200));

        tile.Should().ContainSingle().Which.EndEntryLid.Should().Be(149);
    }

    [Fact]
    public void LaterBlockShouldPreserveTheEarlierPrefixAcrossTileBoundaries()
    {
        // arrange
        Range<long>[] ranges = [new(50, 500), new(100, 120)];

        // act
        var prefix = ConversationRangeTile.NewNormalized(TestChatId, new(60, 90), ranges);
        var suffix = ConversationRangeTile.NewNormalized(TestChatId, new(150, 200), ranges);

        // assert
        prefix.ConversationRanges.Should().Equal(new Range<long>(50, 100));
        prefix.NextConversationRange.Should().Be(new Range<long>(100, 120));
        suffix.ConversationRanges.Should().BeEmpty();
        suffix.PreviousConversationRange.Should().Be(new Range<long>(100, 120));
    }

    [Fact]
    public void TileShouldApplyTheMetadataEndEvenWhenTheSuccessorIsOutsideIt()
    {
        // arrange
        var earlier = new Conversation(ConversationId.New(TestChatId, 50)) { EndEntryLid = 499 };
        var meta = ConversationRangeTile.NewNormalized(TestChatId, new(60, 90), [new(50, 500), new(100, 120)]);

        // act
        var tile = meta.ApplyTo([earlier], new(60, 90));

        // assert
        tile.Should().ContainSingle().Which.EntryLidRange.Should().Be(new Range<long>(50, 100));
        meta.ApplyTo([earlier], new(150, 200)).Should().BeEmpty();
        earlier.EndEntryLid.Should().Be(499);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenEndedCoverageShouldUseFreshSummaryDespiteLaterBlocks(bool hasLaterBlock)
    {
        // arrange
        var live = new Conversation(ConversationId.New(TestChatId, 100)) { EndEntryLid = 249 };
        var meta = ConversationRangeTile.NewNormalized(TestChatId, new(200, 220),
            hasLaterBlock ? [new(100, long.MaxValue), new(230, 240)] : [new(100, long.MaxValue)]);

        // act
        var tile = meta.ApplyTo([live], new(200, 220));

        // assert
        tile.Should().ContainSingle().Which.EndEntryLid.Should().Be(249);
    }

    [Fact]
    public void FiniteProjectionShouldReclassifyAnEndedRangeAsThePreviousNeighbor()
    {
        // arrange
        var meta = ConversationRangeTile.NewNormalized(TestChatId, new(200, 220),
            [new(50, 300), new(100, long.MaxValue), new(150, 170)]);

        // act
        var finite = meta.ToFinite(180, new(200, 220));

        // assert
        finite.ConversationRanges.Should().BeEmpty();
        finite.PreviousConversationRange.Should().Be(new Range<long>(100, 180));
        finite.NextConversationRange.Should().BeNull();
        meta.ConversationRanges.Should().Equal(new Range<long>(100, long.MaxValue));
    }

    [Fact]
    public void FiniteProjectionShouldPreserveAnEntrylessBlockCard()
    {
        var meta = ConversationRangeTile.NewNormalized(TestChatId, new(100, 200), [new(100, long.MaxValue)]);

        var finite = meta.ToFinite(100, new(100, 200));

        finite.ConversationRanges.Should().Equal(new Range<long>(100, 101));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TileShouldRetainTheLegacyWireShape(bool isOpenEnded)
    {
        var end = isOpenEnded ? long.MaxValue : 200;
        var json = $"[\"the-actual-one\",[[100,{end}]],null,[300,400]]";
        var bytes = MessagePackSerializer.ConvertFromJson(json);

        var tile = MessagePackSerializer.Deserialize<ConversationRangeTile>(bytes, MessagePackByteSerializer.DefaultOptions);
        var encoded = MessagePackSerializer.Serialize(tile, MessagePackByteSerializer.DefaultOptions);

        tile.ConversationRanges.Should().Equal(new Range<long>(100, end));
        encoded.Should().Equal(bytes);
    }

    [Fact]
    public void NeighborsShouldProduceTheSameVisibleCoverageAsAllRanges()
    {
        // arrange
        long[] starts = [10, 20, 30, 40];
        long[] ends = [15, 25, 35, 45, 60];
        var combinations = starts.Aggregate(new List<List<Range<long>>> { new() },
            (sets, start) => sets.SelectMany(set => ends.Where(end => end > start)
                .Select(end => new List<Range<long>>(set) { new(start, end) })).ToList());

        foreach (var ranges in combinations) {
            for (var start = 0; start < 70; start += 5) {
                var tile = new Range<long>(start, start + 5);
                var candidates = ranges.Where(r => r.Overlaps(tile)).ToList();
                candidates.AddRange(ranges.Where(r => r.End <= tile.Start).TakeLast(1));
                candidates.AddRange(ranges.Where(r => r.Start >= tile.End).Take(1));

                // act
                var expected = ConversationRangeTile.NewNormalized(TestChatId, tile, ranges);
                var actual = ConversationRangeTile.NewNormalized(TestChatId, tile, candidates);

                // assert
                actual.ConversationRanges.Should().Equal(expected.ConversationRanges);
                actual.PreviousConversationRange.Should().Be(expected.PreviousConversationRange);
                (actual.NextConversationRange?.Start).Should().Be(expected.NextConversationRange?.Start);
            }
        }
    }
}
