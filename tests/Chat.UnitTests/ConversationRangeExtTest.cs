namespace ActualChat.Chat.UnitTests;

public sealed class ConversationRangeExtTest
{
    [Fact]
    public void OpenEndedRangeShouldWinRegardlessOfInputOrderOrDuplicateStarts()
    {
        Range<long>[] ranges = [new(200, 300), new(100, 150), new(50, 400), new(100, long.MaxValue)];

        var merged = ranges.MergeConversationRanges().ToArray();
        var reversed = ranges.Reverse().MergeConversationRanges();

        merged.Should().Equal(new Range<long>(50, 100), new Range<long>(100, long.MaxValue));
        reversed.Should().Equal(merged);
        merged[^1].IsOpenEnded.Should().BeTrue();
        merged[0].IsOpenEnded.Should().BeFalse();
    }

    [Fact]
    public void FiniteRangesShouldKeepTheLaterStartRuleWithoutResumingEarlierSuffixes()
    {
        Range<long>[] ranges = [new(100, 120), new(50, 500), new(100, 130), new(150, 170)];

        var merged = ranges.MergeConversationRanges();

        merged.Should().Equal(new Range<long>(50, 100), new Range<long>(100, 130), new Range<long>(150, 170));
    }

    [Fact]
    public void InvalidRangesShouldNotHideValidRanges()
    {
        Range<long>[] ranges = [new(long.MaxValue, long.MaxValue), new(100, 100), new(30, 20), new(10, 20)];

        ranges.MergeConversationRanges().Should().Equal(new Range<long>(10, 20));
        Array.Empty<Range<long>>().MergeConversationRanges().Should().BeEmpty();
    }

    [Fact]
    public void AnOpenEndedRangeShouldNotBeSplitByAnotherOpenEndedCandidate()
    {
        Range<long>[] ranges = [new(200, long.MaxValue), new(100, long.MaxValue)];

        ranges.MergeConversationRanges().Should().Equal(new Range<long>(100, long.MaxValue));
    }
}
