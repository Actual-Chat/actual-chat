using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class LidRangeSetTest
{
    [Fact]
    public void ShouldMergeAdjacentAndOverlappingRanges()
    {
        // arrange
        var set = new LidRangeSet();

        // act
        set.Add(new Range<long>(10, 12));
        set.Add(new Range<long>(12, 15));
        set.Add(new Range<long>(11, 13));
        set.Add(new Range<long>(20, 21));

        // assert
        set.Intersects(new Range<long>(10, 15)).Should().BeTrue();
        set.Intersects(new Range<long>(14, 16)).Should().BeTrue();
        set.Intersects(new Range<long>(15, 20)).Should().BeFalse();
        set.Intersects(new Range<long>(20, 25)).Should().BeTrue();
        set.Intersects(new Range<long>(25, 30)).Should().BeFalse();
    }

    [Fact]
    public void ShouldIgnoreEmptyRanges()
    {
        // arrange
        var set = new LidRangeSet();

        // act
        set.Add(default);
        set.Add(new Range<long>(5, 5));

        // assert
        set.Intersects(new Range<long>(0, 100)).Should().BeFalse();
    }

    [Fact]
    public void ShouldNotIntersectWithEmptyProbe()
    {
        // arrange
        var set = new LidRangeSet();
        set.Add(new Range<long>(10, 20));

        // act
        var intersectsEmpty = set.Intersects(default);
        var intersectsZeroWidth = set.Intersects(new Range<long>(15, 15));

        // assert
        intersectsEmpty.Should().BeFalse();
        intersectsZeroWidth.Should().BeFalse();
    }

    [Fact]
    public void ShouldBridgeSeveralRangesOnOverlappingAdd()
    {
        // arrange
        var set = new LidRangeSet();
        set.Add(new Range<long>(30, 31));
        set.Add(new Range<long>(10, 11));
        set.Add(new Range<long>(20, 21));

        // act
        set.Add(new Range<long>(10, 21));

        // assert
        set.Intersects(new Range<long>(11, 20)).Should().BeTrue("the gap between the bridged ranges is now witnessed");
        set.Intersects(new Range<long>(21, 30)).Should().BeFalse("the gap before the trailing range must survive");
        set.Intersects(new Range<long>(30, 31)).Should().BeTrue("the trailing range must not be dropped");
        set.Intersects(new Range<long>(31, 40)).Should().BeFalse();
    }
}
