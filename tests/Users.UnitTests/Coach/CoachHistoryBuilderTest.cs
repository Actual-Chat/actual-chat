using ActualChat.Chat;
using ActualChat.Serialization;
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public sealed class CoachHistoryBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CoachScoringSettings Settings = new() { MinScoreWords = 100 };
    private static readonly Range<Moment> Range = new(Start, Start + TimeSpan.FromDays(30));

    [Fact]
    public void HistoryShouldCompressInactivityAndKeepZeroMeasurementsAndRealGaps()
    {
        // arrange
        var days = new[] {
            Day(0, 100, 0), Day(1, 0, 0), Day(3, 20, 1), Day(9, 900, 90),
            Day(10, 1_000, 200) with { Language = "ru" },
        };

        // act
        var history = CoachHistoryBuilder.Build(Range, CoachMetricKind.Fillers, days, Settings, "en");

        // assert
        history.Days.Select(d => d.Day).Should().Equal(Start, Start + TimeSpan.FromDays(9));
        history.Days.Select(d => d.GapDays).Should().Equal(0, 8);
        history.Days[0].Value.Should().Be(0);
        history.Value.Should().BeApproximately(91d / 1_020, 1e-9);
        history.MeasuredWords.Should().Be(1_020);
    }

    [Fact]
    public void ShortDaysShouldStillContributeToAnEligiblePeriodWithoutInventingDailyPoints()
    {
        // arrange
        var days = new[] { Day(0, 50, 5), Day(1, 50, 0) };

        // act
        var history = CoachHistoryBuilder.Build(Range, CoachMetricKind.Fillers, days, Settings, "en");

        // assert
        history.Days.Should().BeEmpty();
        history.Value.Should().Be(0.05);
    }

    [Fact]
    public void UntaggedAndUnmeasuredSpeechShouldNotBecomeZeroValues()
    {
        // arrange
        var day = Day(0, 300, 0) with { TaggedWords = 0 };

        // act
        var fillers = CoachHistoryBuilder.Build(Range, CoachMetricKind.Fillers, [day], Settings, "en");
        var pace = CoachHistoryBuilder.Build(Range, CoachMetricKind.Pace, [day], Settings, "en");

        // assert
        fillers.Value.Should().BeNull();
        fillers.Days.Should().BeEmpty();
        pace.Value.Should().BeNull();
        pace.Days.Should().BeEmpty();
    }

    [Fact]
    public void PaceHistoryShouldUseExactClassifiedWordAndTimeTotalsInsteadOfBinCenters()
    {
        // arrange
        var summary = new SpeechPaceSummary {
            Durations = new ApiMap<int, long>(new Dictionary<int, long> { [34] = 30_000 }),
            MeasuredEntries = 1, AudioMilliseconds = 36_000, ValidWords = 96,
            UnclassifiedWords = 10, UnclassifiedMilliseconds = 6_000,
        };
        var day = Day(0, 1_000, 0) with { Pace = summary };
        var settings = new CoachScoringSettings { MinScoreWords = 20 };

        // act
        var history = CoachHistoryBuilder.Build(Range, CoachMetricKind.Pace, [day], settings, "en");

        // assert
        history.Value.Should().Be(172);
        history.MeasuredWords.Should().Be(86);
        history.MeasuredSeconds.Should().Be(30);
        history.Days.Should().ContainSingle().Which.Value.Should().Be(172);
    }

    [Fact]
    public void CalendarRangesShouldUseUtcDaysMondayWeeksAndCalendarMonths()
    {
        // arrange
        Moment now = new DateTime(2024, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        Moment leapDay = new DateTime(2024, 2, 29, 15, 0, 0, DateTimeKind.Utc);

        // act
        var day = CoachHistoryRanges.Get(CoachHistoryPeriod.Day, leapDay, now);
        var week = CoachHistoryRanges.Get(CoachHistoryPeriod.Week, leapDay, now);
        var month = CoachHistoryRanges.Get(CoachHistoryPeriod.Month, leapDay, now);

        // assert
        day.Start.ToDateTime().Hour.Should().Be(0);
        day.End.Should().Be(now - TimeSpan.FromHours(12));
        week.Start.ToDateTime().DayOfWeek.Should().Be(DayOfWeek.Monday);
        week.End.Should().Be(now, "current periods stop at the captured request time");
        month.Start.ToDateTime().Day.Should().Be(1);
        month.End.Should().Be(day.End);
        CoachHistoryRanges.Move(CoachHistoryPeriod.Month, month.Start, 1).Should().Be(day.End);
    }

    [Fact]
    public void CalendarRangesShouldRejectFutureAnchorsAndUnknownPeriods()
    {
        // act
        var future = () => CoachHistoryRanges.Get(CoachHistoryPeriod.Day, Start + TimeSpan.FromDays(1), Start);
        var unknown = () => CoachHistoryRanges.Get((CoachHistoryPeriod)99, Start, Start);

        // assert
        future.Should().Throw<ArgumentOutOfRangeException>();
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HistoryContractShouldPreserveZeroPointsAndCoverageAcrossSerializers()
    {
        // arrange
        var history = CoachHistoryBuilder.Build(Range, CoachMetricKind.WeakWords, [Day(0, 100, 0)], Settings, "en");
        var serializer = new VersionedByteSerializer([MessagePackByteSerializer.Default]);

        // act
        using var bytes = serializer.Write(history);
        var binary = (CoachSkillHistory)serializer.Read(bytes.WrittenMemory, typeof(CoachSkillHistory), out _)!;
        var json = SystemJsonSerializer.Default.Read<CoachSkillHistory>(SystemJsonSerializer.Default.Write(history));

        // assert
        binary.Should().BeEquivalentTo(history);
        json.Should().BeEquivalentTo(history);
        binary.Days.Should().ContainSingle().Which.Value.Should().Be(0);
    }

    private static CoachDay Day(int offset, int words, int fillers)
        => new(Start + TimeSpan.FromDays(offset)) {
            Language = "en", Words = words, TaggedWords = words, SpeechSeconds = words / 2d,
            Fillers = fillers, Entries = words > 0 ? 1 : 0,
        };
}
