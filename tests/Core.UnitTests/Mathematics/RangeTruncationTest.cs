namespace ActualChat.Core.UnitTests.Mathematics;

public sealed class RangeTruncationTest
{
    [Fact]
    public void LaterStartsShouldOwnOverlapsWithoutRestoringEarlierSuffixes()
    {
        // arrange
        Range<long>[] ranges = [new(30, 40), new(10, 100), new(20, 50), new(110, 120)];

        // act
        var result = ranges.TruncateOverlaps();

        // assert
        result.Should().Equal(new Range<long>(10, 20), new Range<long>(20, 30),
            new Range<long>(30, 40), new Range<long>(110, 120));
    }

    [Fact]
    public void EqualStartsShouldKeepTheLargestRangeAndIgnoreEmptyCandidates()
    {
        // arrange
        Range<long>[] ranges = [new(10, 20), new(10, 40), new(20, 20), new(50, 40), new(40, 50)];

        // act
        var result = ranges.TruncateOverlaps();

        // assert
        result.Should().Equal(new Range<long>(10, 40), new Range<long>(40, 50));
    }
}
