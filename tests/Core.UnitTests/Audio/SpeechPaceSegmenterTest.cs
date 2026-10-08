using ActualChat.Audio;

namespace ActualChat.Core.UnitTests.Audio;

public sealed class SpeechPaceSegmenterTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ConstantSpeechShouldProduceNonOverlappingSegments()
    {
        // arrange
        var words = Words(75, 400);

        // act
        var result = SpeechPaceSegmenter.Compute(words, 30_000);

        // assert
        result.Segments.Should().HaveCount(3);
        result.Segments.Should().OnlyContain(x => x.Words == 25 && x.WordsPerMinute == 150);
        result.ValidWords.Should().Be(75);
        result.UnclassifiedWords.Should().Be(0);
        result.UnmappedMilliseconds.Should().Be(0);
        Reconcile(result, 30_000);
    }

    [Fact]
    public void ShortBoundaryGapsShouldBeAssignedExactlyOnce()
    {
        // arrange
        var words = Words(50, 350, gap: 50);

        // act
        var result = SpeechPaceSegmenter.Compute(words, 19_950);

        // assert
        result.Segments.Should().HaveCount(2);
        result.Segments[1].TimeRange.Start.Should().Be(result.Segments[0].TimeRange.End);
        result.Segments.Sum(x => x.Words).Should().Be(50);
        result.PauseMilliseconds.Should().Be(0);
        Reconcile(result, 19_950);
    }

    [Fact]
    public void LongPauseShouldSeparateBlocksAndExcludeSilence()
    {
        // arrange
        var words = Words(25, 400).Concat(Words(25, 400, 14_000, 125)).ToArray();

        // act
        var result = SpeechPaceSegmenter.Compute(words, 26_000);

        // assert
        result.Segments.Should().HaveCount(2);
        result.PauseMilliseconds.Should().Be(4_000);
        result.UnmappedMilliseconds.Should().Be(2_000);
        result.Segments[1].TimeRange.Should().Be(new Range<int>(14_000, 24_000));
        Reconcile(result, 26_000);
    }

    [Fact]
    public void UndersizedTailShouldMergeWithinItsBlock()
    {
        // arrange
        var words = Words(27, 400);

        // act
        var result = SpeechPaceSegmenter.Compute(words, 10_800);

        // assert
        result.Segments.Should().ContainSingle();
        result.Segments[0].Words.Should().Be(27);
        result.UnclassifiedWords.Should().Be(0);
        Reconcile(result, 10_800);
    }

    [Fact]
    public void UndersizedTailShouldNotMergeAcrossAPause()
    {
        // arrange
        var words = Words(25, 400).Concat(Words(2, 400, 12_000, 125)).ToArray();

        // act
        var result = SpeechPaceSegmenter.Compute(words, 12_800);

        // assert
        result.Segments.Should().ContainSingle();
        result.Segments[0].Words.Should().Be(25);
        result.UnclassifiedWords.Should().Be(2);
        result.UnclassifiedMilliseconds.Should().Be(800);
        result.PauseMilliseconds.Should().Be(2_000);
        Reconcile(result, 12_800);
    }

    [Fact]
    public void ExcessivelyLongTailShouldRemainUnclassified()
    {
        // arrange
        var tail = new TimedSpeechWord((125, 129), (10_000, 31_000));
        var words = Words(25, 400).Append(tail).ToArray();

        // act
        var result = SpeechPaceSegmenter.Compute(words, 31_000);

        // assert
        result.Segments.Should().ContainSingle();
        result.UnclassifiedWords.Should().Be(1);
        result.UnclassifiedMilliseconds.Should().Be(21_000);
        Reconcile(result, 31_000);
    }

    [Theory]
    [InlineData(2, 1_000)]
    [InlineData(4, 1_000)]
    [InlineData(5, 400)]
    public void InsufficientBlocksShouldNotProduceConfidentPace(int count, int duration)
    {
        // arrange
        var words = Words(count, duration);

        // act
        var result = SpeechPaceSegmenter.Compute(words, count * duration);

        // assert
        result.Segments.Should().BeEmpty();
        result.UnclassifiedWords.Should().Be(count);
        result.UnclassifiedMilliseconds.Should().Be(count * duration);
        Reconcile(result, count * duration);
    }

    [Fact]
    public void InvalidTimingShouldBreakSegmentsWithoutReusingSpeech()
    {
        // arrange
        var words = Words(50, 400);
        words[25] = words[25] with { TimeRange = new Range<int>(0, 400) };

        // act
        var result = SpeechPaceSegmenter.Compute(words, 20_000);

        // assert
        result.RejectedWords.Should().Be(1);
        result.ValidWords.Should().Be(49);
        result.Segments.Should().HaveCount(2);
        result.UnmappedMilliseconds.Should().Be(400);
        result.Segments[1].TimeRange.Start.Should().Be(10_400);
        Reconcile(result, 20_000);
    }

    [Theory]
    [InlineData(-1, 1_000)]
    [InlineData(0, 0)]
    [InlineData(500, 200)]
    [InlineData(0, 2_000)]
    public void InvalidWordIntervalsShouldRemainUnavailable(int start, int end)
    {
        // arrange
        TimedSpeechWord[] words = [new((0, 4), (start, end))];

        // act
        var result = SpeechPaceSegmenter.Compute(words, 1_000);

        // assert
        result.RejectedWords.Should().Be(1);
        result.Segments.Should().BeEmpty();
        result.UnmappedMilliseconds.Should().Be(1_000);
        Reconcile(result, 1_000);
    }

    [Fact]
    public void OverlappingTextShouldNotCountWordsTwice()
    {
        // arrange
        var words = Words(25, 400);
        words[10] = words[10] with { TextRange = words[9].TextRange };

        // act
        var result = SpeechPaceSegmenter.Compute(words, 10_000);

        // assert
        result.RejectedWords.Should().Be(1);
        result.ValidWords.Should().Be(24);
        result.UnmappedMilliseconds.Should().Be(400);
        Reconcile(result, 10_000);
    }

    [Fact]
    public void EmptyInputShouldPreserveUnavailableDuration()
    {
        // arrange
        TimedSpeechWord[] words = [];

        // act
        var result = SpeechPaceSegmenter.Compute(words, 4_000);

        // assert
        result.ValidWords.Should().Be(0);
        result.Segments.Should().BeEmpty();
        result.UnmappedMilliseconds.Should().Be(4_000);
        Reconcile(result, 4_000);
    }

    [Theory]
    [InlineData(0, 3_000, 5, 1_000)]
    [InlineData(10_000, 0, 5, 1_000)]
    [InlineData(10_000, 11_000, 5, 1_000)]
    [InlineData(10_000, 3_000, 0, 1_000)]
    [InlineData(10_000, 3_000, 5, 0)]
    public void InvalidOptionsShouldBeRejected(int target, int minimum, int words, int pause)
    {
        // arrange
        var options = new SpeechPaceSegmenter.Options {
            TargetMilliseconds = target, MinMilliseconds = minimum, MinWords = words, PauseMilliseconds = pause,
        };

        // act
        var act = () => SpeechPaceSegmenter.Compute([], 1_000, options);

        // assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // Private methods

    private static TimedSpeechWord[] Words(
        int count, int duration, int start = 0, int textStart = 0,
        int gap = 0)
        => Enumerable.Range(0, count)
            .Select(i => new TimedSpeechWord(
                (textStart + i * 5, textStart + i * 5 + 4),
                (start + i * (duration + gap), start + i * (duration + gap) + duration)))
            .ToArray();

    private static void Reconcile(SpeechPaceAnalysis result, int duration)
    {
        result.Segments.Sum(x => x.Words).Should().Be(result.ValidWords - result.UnclassifiedWords);
        var accounted = result.Segments.Sum(x => x.DurationMilliseconds) + result.UnclassifiedMilliseconds
            + result.PauseMilliseconds + result.UnmappedMilliseconds;
        accounted.Should().Be(duration, "every millisecond must have exactly one coverage category");
        for (var i = 1; i < result.Segments.Length; i++)
            result.Segments[i].TimeRange.Start.Should().BeGreaterThanOrEqualTo(result.Segments[i - 1].TimeRange.End);
    }
}
