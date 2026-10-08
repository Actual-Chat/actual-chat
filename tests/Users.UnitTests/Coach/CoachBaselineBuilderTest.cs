using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public sealed class CoachBaselineBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CoachScoringSettings Settings = new() { MinScoreWords = 100 };
    private static readonly Range<Moment> Range = new(Start, Start + TimeSpan.FromDays(1));

    [Fact]
    public void CaptureShouldKeepWeightedRawTotalsZeroValuesAndBoundedProvenance()
    {
        // arrange
        var records = new[] { Entry(0, 100, 0), Entry(1, 900, 0) };

        // act
        var baseline = CoachBaselineBuilder.Capture(Range, Range.End, CoachMetricKind.Fillers,
            "en", records, Settings);
        using var bytes = KvasSerializer.Default.Write(baseline);
        var restored = (CoachBaseline)KvasSerializer.Default.Read(bytes.WrittenMemory, typeof(CoachBaseline), out _)!;

        // assert
        baseline.Value.Should().Be(0);
        baseline.Numerator.Should().Be(0);
        baseline.Denominator.Should().Be(1_000);
        baseline.Sources.Should().HaveCount(2);
        baseline.History.Days.Should().ContainSingle();
        restored.Should().BeEquivalentTo(baseline);
    }

    [Fact]
    public void CaptureShouldRejectMissingMeasurementsAndExcessiveSources()
    {
        // arrange
        var untagged = Entry(0, 500, 0) with { Entry = Entry(0, 500, 0).Entry! with { IsTagged = false } };
        var many = Enumerable.Range(0, CoachBaselineBuilder.MaxSources + 1).Select(i => Entry(i, 100, 0));

        // act
        var missing = () => CoachBaselineBuilder.Capture(Range, Range.End, CoachMetricKind.Fillers,
            "en", [untagged], Settings);
        var excessive = () => CoachBaselineBuilder.Capture(Range, Range.End, CoachMetricKind.Fillers,
            "en", many, Settings);

        // assert
        missing.Should().Throw<InvalidOperationException>();
        excessive.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ComparisonShouldUseOnlyLaterFinalizedSpeechAndRetainTheCutoffDay()
    {
        // arrange
        var cutoff = Start + TimeSpan.FromHours(12);
        var baseline = CoachBaselineBuilder.Capture(new Range<Moment>(Start, cutoff), cutoff,
            CoachMetricKind.Fillers, "en", [Entry(0, 100, 10)], Settings);
        var straddling = Entry(1, 100, 10) with { OccurredAt = cutoff - TimeSpan.FromSeconds(10) };
        var later = Entry(2, 100, 0) with { OccurredAt = cutoff + TimeSpan.FromHours(1) };
        var unfinished = Entry(3, 100, 5) with { OccurredAt = Range.End - TimeSpan.FromSeconds(10) };

        // act
        var comparison = CoachBaselineBuilder.Compare(baseline, Range, Range.End,
            [straddling, later, unfinished], Settings);

        // assert
        comparison.History.Range.Start.Should().Be(cutoff);
        comparison.History.Value.Should().Be(0);
        comparison.History.Days.Should().ContainSingle().Which.Day.Should().Be(Start);
        comparison.History.MeasuredWords.Should().Be(100);
        comparison.OmittedSeconds.Should().Be(100);
        comparison.Change.Should().Be(-0.1);
        comparison.IsBetter.Should().BeTrue();
        baseline.Value.Should().Be(0.1);
    }

    [Fact]
    public void InactivityInvalidationAndIncompatibleVersionsShouldNotProduceComparisons()
    {
        // arrange
        var baseline = CoachBaselineBuilder.Capture(Range, Range.End, CoachMetricKind.Fillers,
            "en", [Entry(0, 100, 0)], Settings);
        var laterRange = new Range<Moment>(Range.End, Range.End + TimeSpan.FromDays(1));

        // act
        var empty = CoachBaselineBuilder.Compare(baseline, laterRange, laterRange.End, [], Settings);
        var invalid = CoachBaselineBuilder.Compare(baseline with { InvalidatedAt = Range.End },
            laterRange, laterRange.End, [], Settings);
        var incompatible = CoachBaselineBuilder.Compare(baseline with { MeasurementVersion = 2 },
            laterRange, laterRange.End, [], Settings);

        // assert
        empty.Change.Should().BeNull();
        empty.IsBetter.Should().BeNull();
        invalid.Change.Should().BeNull();
        incompatible.Change.Should().BeNull();
    }

    [Fact]
    public void PaceBaselineShouldKeepExactTimeTotalsAndCompareDistanceToTheCurrentRange()
    {
        // arrange
        var source = Entry(0, 200, 0);
        var measured = source with { Entry = source.Entry! with {
            DurationSeconds = 60,
            Pace = new SpeechPaceMeasurement(1, 60_000, new SpeechPaceAnalysis([
                new SpeechPaceSegment((0, 800), (0, 60_000), 200),
            ], 200, 0, 0, 0, 0, 0)),
        } };
        var baseline = CoachBaselineBuilder.Capture(Range, Range.End, CoachMetricKind.Pace,
            "en", [measured], Settings);
        var laterRange = new Range<Moment>(Range.End, Range.End + TimeSpan.FromDays(1));
        var later = measured with { OccurredAt = Range.End, Entry = measured.Entry with {
            Pace = new SpeechPaceMeasurement(1, 60_000, new SpeechPaceAnalysis([
                new SpeechPaceSegment((0, 600), (0, 60_000), 150),
            ], 150, 0, 0, 0, 0, 0)),
        } };
        var settings = new CoachScoringSettings { MinScoreWords = 100 };
        settings.PaceByLanguage["en"].Slow = 100;
        settings.PaceByLanguage["en"].Fast = 140;

        // act
        var comparison = CoachBaselineBuilder.Compare(baseline, laterRange, laterRange.End, [later], settings);

        // assert
        baseline.Value.Should().Be(200);
        baseline.Numerator.Should().Be(200);
        baseline.Denominator.Should().Be(60_000);
        baseline.History.Pace.Distribution.AboveMilliseconds.Should().Be(60_000);
        baseline.History.Pace.Moments.Should().BeEmpty();
        comparison.History.Pace.Moments.Should().ContainSingle();
        comparison.History.Value.Should().Be(150);
        comparison.HasRangeChanged.Should().BeTrue();
        comparison.IsBetter.Should().BeTrue();
        comparison.History.Pace.Distribution.AboveMilliseconds.Should().Be(60_000);
    }

    private static CoachRecord Entry(int index, int words, int fillers)
        => new(CoachRecordKind.Entry, "source-" + index, UserId.New(), GroupChatId.New(), Start) {
            Version = 1,
            Entry = new CoachEntryRecord(1, "en", 50, 50, words, 1, 0, 0, 1, 0, 0,
                true, 0, fillers, 0, 0, []),
        };
}
