namespace ActualChat.Core.UnitTests.Mathematics;

public sealed class RangeTruncationTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterStartsShouldOwnOverlapsWithoutRestoringEarlierSuffixes(bool mustKeepOpenEnded)
    {
        // arrange
        Range<long>[] ranges = [new(30, 40), new(10, 100), new(20, 50), new(110, 120)];

        // act
        var result = ranges.TruncateOverlaps(mustKeepOpenEnded);

        // assert
        result.Should().Equal(new Range<long>(10, 20), new Range<long>(20, 30),
            new Range<long>(30, 40), new Range<long>(110, 120));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualStartsShouldKeepTheLargestRangeAndIgnoreEmptyCandidates(bool mustKeepOpenEnded)
    {
        // arrange
        Range<long>[] ranges = [new(10, 20), new(10, 40), new(20, 20), new(50, 40), new(40, 50)];

        // act
        var result = ranges.TruncateOverlaps(mustKeepOpenEnded);

        // assert
        result.Should().Equal(new Range<long>(10, 40), new Range<long>(40, 50));
    }

    [Fact]
    public void KeepingOpenEndedShouldPreserveTheFirstOpenRangeRegardlessOfInputOrder()
    {
        // arrange
        Range<long>[] ranges = [
            new(200, long.MaxValue), new(100, 150), new(50, 400), new(100, long.MaxValue), new(300, 400),
        ];
        Range<long>[] expected = [new(50, 100), new(100, long.MaxValue)];

        // act
        var result = ranges.TruncateOverlaps(mustKeepOpenEnded: true);
        var reversed = ranges.Reverse().TruncateOverlaps(mustKeepOpenEnded: true);

        // assert
        result.Should().Equal(expected);
        reversed.Should().Equal(expected);
    }

    [Fact]
    public void OpenEndedRangesShouldRemainTruncatableByDefault()
    {
        // arrange
        Range<long>[] ranges = [new(100, long.MaxValue), new(200, 300)];
        Range<long>[] expected = [new(100, 200), new(200, 300)];

        // act
        var result = ranges.TruncateOverlaps();
        var explicitDefault = ranges.TruncateOverlaps(mustKeepOpenEnded: false);

        // assert
        result.Should().Equal(expected);
        explicitDefault.Should().Equal(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyAndInvalidRangesShouldBeIgnored(bool mustKeepOpenEnded)
    {
        // arrange
        Range<long>[] ranges = [new(long.MaxValue, long.MaxValue), new(100, 100), new(30, 20)];

        // act
        var result = ranges.TruncateOverlaps(mustKeepOpenEnded);
        var empty = Array.Empty<Range<long>>().TruncateOverlaps(mustKeepOpenEnded);

        // assert
        result.Should().BeEmpty();
        empty.Should().BeEmpty();
    }
}
