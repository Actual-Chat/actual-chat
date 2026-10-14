using ActualChat.Audio;

namespace ActualChat.Core.UnitTests.Audio;

public sealed class SpeechPaceHistogramTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ClassificationShouldWeightDurationRatherThanRecordCount()
    {
        // arrange
        SpeechPaceSegment[] segments = [Segment(120, 60_000), Segment(300, 120_000), Segment(180, 60_000)];

        // act
        var result = SpeechPaceHistogram.Classify(segments, 130, 170);

        // assert
        result.BelowMilliseconds.Should().Be(60_000);
        result.WithinMilliseconds.Should().Be(120_000);
        result.AboveMilliseconds.Should().Be(60_000);
        result.WithinRate.Should().Be(0.5);
    }

    [Fact]
    public void ExactTargetBoundariesShouldBeWithinRange()
    {
        // arrange
        SpeechPaceSegment[] segments = [Segment(130, 60_000), Segment(170, 60_000)];

        // act
        var result = SpeechPaceHistogram.Classify(segments, 130, 170);

        // assert
        result.WithinMilliseconds.Should().Be(120_000);
        result.BelowMilliseconds.Should().Be(0);
        result.AboveMilliseconds.Should().Be(0);
    }

    [Fact]
    public void TargetChangesShouldReclassifyWithoutChangingSourceMeasurements()
    {
        // arrange
        SpeechPaceSegment[] segments = [Segment(120, 60_000)];
        var original = segments[0];

        // act
        var english = SpeechPaceHistogram.Classify(segments, 130, 170);
        var russian = SpeechPaceHistogram.Classify(segments, 100, 140);

        // assert
        english.BelowMilliseconds.Should().Be(60_000);
        russian.WithinMilliseconds.Should().Be(60_000);
        segments[0].Should().Be(original);
    }

    [Fact]
    public void BoundariesInsideOneBinShouldUseRawSegments()
    {
        // arrange
        SpeechPaceSegment[] segments = [Segment(217, 100_000), Segment(218, 100_000)];

        // act
        var histogram = SpeechPaceHistogram.Build(segments, 5);
        var result = SpeechPaceHistogram.Classify(segments, 130.5, 140);

        // assert
        histogram.Durations.Should().ContainSingle();
        histogram.TotalMilliseconds.Should().Be(200_000);
        result.BelowMilliseconds.Should().Be(100_000);
        result.WithinMilliseconds.Should().Be(100_000);
    }

    [Fact]
    public void MultimodalSpeechShouldNotBecomeAComfortableAverage()
    {
        // arrange
        SpeechPaceSegment[] segments = [Segment(120, 60_000), Segment(180, 60_000)];

        // act
        var histogram = SpeechPaceHistogram.Build(segments);
        var result = SpeechPaceHistogram.Classify(segments, 130, 170);

        // assert
        histogram.Durations.Keys.Should().Equal(24, 36);
        result.WithinRate.Should().Be(0);
        result.TotalMilliseconds.Should().Be(120_000);
    }

    [Fact]
    public void SparseHistogramShouldPreserveTailsAndDuration()
    {
        // arrange
        SpeechPaceSegment[] segments = [Segment(1, 600_000), Segment(10_000, 60_000)];

        // act
        var histogram = SpeechPaceHistogram.Build(segments);

        // assert
        histogram.Durations.Should().HaveCount(2);
        histogram.Durations.Keys.Should().Equal(0, 2_000);
        histogram.TotalMilliseconds.Should().Be(660_000);
    }

    [Fact]
    public void EmptyDistributionShouldNotClaimZeroOrPerfectPerformance()
    {
        // arrange
        SpeechPaceSegment[] segments = [];

        // act
        var result = SpeechPaceHistogram.Classify(segments, 130, 170);
        var histogram = SpeechPaceHistogram.Build(segments);

        // assert
        result.WithinRate.Should().BeNull();
        result.TotalMilliseconds.Should().Be(0);
        histogram.Durations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1, 170)]
    [InlineData(170, 130)]
    [InlineData(double.NaN, 170)]
    [InlineData(130, double.PositiveInfinity)]
    public void InvalidTargetsShouldBeRejected(double low, double high)
    {
        // arrange
        var segments = new[] { Segment(150, 60_000) };

        // act
        var act = () => SpeechPaceHistogram.Classify(segments, low, high);

        // assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InvalidSegmentsShouldNotBeCountedAsWithinTarget()
    {
        // arrange
        SpeechPaceSegment[] segments = [new((0, 4), (0, 0), 1)];

        // act
        var classify = () => SpeechPaceHistogram.Classify(segments, 130, 170);
        var histogram = () => SpeechPaceHistogram.Build(segments);

        // assert
        classify.Should().Throw<ArgumentOutOfRangeException>();
        histogram.Should().Throw<ArgumentOutOfRangeException>();
    }

    // Private methods

    private static SpeechPaceSegment Segment(int words, int milliseconds)
        => new ((0, words * 5), (0, milliseconds), words);
}
