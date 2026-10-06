using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachProgressBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly CoachScoringSettings S = new();
    private static readonly Moment Now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static CoachDay Day(Moment day, int words, int fillers, double speechSeconds, double monologue = 0)
        => new (day) {
            Words = words, TaggedWords = words, FilledPauses = fillers, SpeechSeconds = speechSeconds,
            Sentences = Math.Max(1, words / 10), Entries = 1, Runs = monologue > 0 ? 1 : 0,
            LongestMonologueSeconds = monologue,
        };

    [Fact]
    public void WeekDeltasShouldSayWhichDirectionIsBetter()
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 70, 600);
        var now = Day(UsageDay.DayOf(Now), 1000, 40, 500);

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en-US");

        // assert
        var fillers = deltas.Single(d => d.Kind == CoachMetricKind.Fillers);
        fillers.Previous.Should().BeApproximately(0.07, 1e-9);
        fillers.Current.Should().BeApproximately(0.04, 1e-9);
        fillers.IsBetter.Should().BeTrue();
        deltas.Single(d => d.Kind == CoachMetricKind.Pace).IsBetter
            .Should().BeTrue("100 → 120 wpm moves toward the band");
    }

    [Fact]
    public void WeekDeltasShouldBeUnknownBelowTheWordFloor()
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 50, 5, 60);
        var now = Day(UsageDay.DayOf(Now), 1000, 40, 500);

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en-US");

        // assert
        deltas.Should().OnlyContain(d => d.Previous == null && d.IsBetter == null);
    }

    [Theory]
    [InlineData(CoachLanguageLevel.Native)]
    [InlineData(CoachLanguageLevel.Learning)]
    public void WeekDeltasShouldIncludeAllRecentSummarySkills(CoachLanguageLevel level)
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 70, 600);
        var now = Day(UsageDay.DayOf(Now), 1000, 40, 500) with { WeakWords = 30 };

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, level, S, "en");

        // assert
        deltas.Select(d => d.Kind).Should().Contain([
            CoachMetricKind.Fillers, CoachMetricKind.Pace, CoachMetricKind.WeakWords,
        ]);
        deltas.Select(d => d.Kind).Should().OnlyHaveUniqueItems();
        deltas.Single(d => d.Kind == CoachMetricKind.WeakWords).Current.Should().BeApproximately(0.03, 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(199)]
    public void WeekDeltasShouldHideCurrentValuesBelowTheWordFloor(int words)
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 70, 600);
        var now = Day(UsageDay.DayOf(Now), words, 0, 60);

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en");

        // assert
        deltas.Should().OnlyContain(d => d.Current == null && d.IsBetter == null && d.Band == CoachBand.None);
        deltas.Single(d => d.Kind == CoachMetricKind.Pace).Previous.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(199, false)]
    [InlineData(200, true)]
    public void WeekDeltasShouldRequireEnoughTaggedWordsForTaggerRates(int taggedWords, bool isEligible)
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 70, 600);
        var now = Day(UsageDay.DayOf(Now), 1000, 0, 500) with { TaggedWords = taggedWords };

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en");

        // assert
        foreach (var kind in new[] { CoachMetricKind.Fillers, CoachMetricKind.WeakWords }) {
            var delta = deltas.Single(d => d.Kind == kind);
            delta.Current.Should().Be(isEligible ? 0d : null);
            delta.Band.Should().Be(isEligible ? CoachBand.Good : CoachBand.None);
            delta.IsBetter.Should().Be(isEligible && kind == CoachMetricKind.Fillers ? true : null);
        }
        deltas.Single(d => d.Kind == CoachMetricKind.Pace).Current.Should().Be(120);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(199)]
    public void WeekDeltasShouldHideAnInsufficientTaggedBaseline(int taggedWords)
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 0, 600)
            with { TaggedWords = taggedWords };
        var now = Day(UsageDay.DayOf(Now), 1000, 40, 500) with { WeakWords = 30 };

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en");

        // assert
        foreach (var kind in new[] { CoachMetricKind.Fillers, CoachMetricKind.WeakWords }) {
            var delta = deltas.Single(d => d.Kind == kind);
            delta.Previous.Should().BeNull();
            delta.Current.Should().NotBeNull();
            delta.IsBetter.Should().BeNull();
        }
        deltas.Single(d => d.Kind == CoachMetricKind.Pace).Previous.Should().Be(100);
    }

    [Fact]
    public void WeekDeltasShouldPreserveRatesWhenOnlyPartOfTheSpeechIsTagged()
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 40, 600)
            with { TaggedWords = 400, WeakWords = 20 };
        var now = Day(UsageDay.DayOf(Now), 1000, 20, 500)
            with { TaggedWords = 200, WeakWords = 10 };

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en");

        // assert
        var fillers = deltas.Single(d => d.Kind == CoachMetricKind.Fillers);
        fillers.Previous.Should().BeApproximately(0.1, 1e-9);
        fillers.Current.Should().BeApproximately(0.1, 1e-9);
        fillers.IsBetter.Should().BeNull();
        var weak = deltas.Single(d => d.Kind == CoachMetricKind.WeakWords);
        weak.Current.Should().BeApproximately(0.05, 1e-9);
        weak.IsBetter.Should().BeNull();
    }

    [Fact]
    public void WeekDeltasShouldPreserveAnUnavailableConversationRate()
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 0, 600);
        var now = Day(UsageDay.DayOf(Now), 1000, 0, 500);

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en");

        // assert
        var interruptions = deltas.Single(d => d.Kind == CoachMetricKind.Interruptions);
        interruptions.Previous.Should().BeNull();
        interruptions.Current.Should().BeNull();
        interruptions.IsBetter.Should().BeNull();
    }

    [Theory]
    [InlineData(70, 70, null)]
    [InlineData(70, 66, null)]
    [InlineData(70, 65, true)]
    [InlineData(70, 64, true)]
    [InlineData(70, 74, null)]
    [InlineData(70, 75, false)]
    [InlineData(70, 76, false)]
    public void RateDeltasShouldTreatChangesBelowHalfAPercentagePointAsStable(
        int previous, int current, bool? isBetter)
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, previous, 600)
            with { WeakWords = previous };
        var now = Day(UsageDay.DayOf(Now), 1000, current, 500) with { WeakWords = current };

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en");

        // assert
        foreach (var kind in new[] { CoachMetricKind.Fillers, CoachMetricKind.WeakWords }) {
            var delta = deltas.Single(d => d.Kind == kind);
            delta.IsBetter.Should().Be(isBetter);
            delta.Previous.Should().BeApproximately(previous / 1000d, 1e-9);
            delta.Current.Should().BeApproximately(current / 1000d, 1e-9);
        }
    }

    [Theory]
    [InlineData(120, 124, null)]
    [InlineData(120, 125, true)]
    [InlineData(120, 126, true)]
    [InlineData(180, 184, null)]
    [InlineData(180, 185, false)]
    [InlineData(180, 186, false)]
    [InlineData(130, 174, null)]
    [InlineData(130, 175, false)]
    [InlineData(175, 170, true)]
    [InlineData(174, 170, null)]
    [InlineData(120, 176, null)]
    [InlineData(140, 150, null)]
    [InlineData(120, 180, null)]
    [InlineData(100, 120, true)]
    [InlineData(150, 180, false)]
    [InlineData(150, 150, null)]
    public void PaceDeltaShouldCompareDistanceFromTheComfortableRange(
        double previous, double current, bool? isBetter)
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 0, 60_000 / previous);
        var now = Day(UsageDay.DayOf(Now), 1000, 0, 60_000 / current);

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en");

        // assert
        deltas.Single(d => d.Kind == CoachMetricKind.Pace).IsBetter.Should().Be(isBetter);
    }

    [Theory]
    [InlineData(0.75, 1.25, null)]
    [InlineData(0.25, 1.75, null)]
    [InlineData(0.25, 0.75, true)]
    [InlineData(1, 2, false)]
    public void TurnTakingDeltaShouldCompareDistanceFromTheComfortableRange(
        double previous, double current, bool? isBetter)
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 0, 600)
            with { FairShareSeconds = 100, OwnSpeechSeconds = previous * 100 };
        var now = Day(UsageDay.DayOf(Now), 1000, 0, 500)
            with { FairShareSeconds = 100, OwnSpeechSeconds = current * 100 };

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en");

        // assert
        deltas.Single(d => d.Kind == CoachMetricKind.TurnTaking).IsBetter.Should().Be(isBetter);
    }

    [Fact]
    public void WeekDeltasShouldUseTheConfiguredWordFloor()
    {
        // arrange
        var settings = new CoachScoringSettings { MinScoreWords = 400 };
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 400, 40, 200);
        var now = Day(UsageDay.DayOf(Now), 400, 20, 200) with { TaggedWords = 399 };

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, settings, "en");

        // assert
        deltas.Single(d => d.Kind == CoachMetricKind.Fillers).Current.Should().BeNull();
        deltas.Single(d => d.Kind == CoachMetricKind.Pace).Current.Should().Be(120);
    }

    [Fact]
    public void MilestonesShouldDateTheFirstDayThatSatisfiesThem()
    {
        // arrange
        var d0 = UsageDay.DayOf(Now) - TimeSpan.FromDays(9);
        var days = Enumerable.Range(0, 10).Select(i => Day(d0 + TimeSpan.FromDays(i), 150, 2, 100)).ToList();

        // act
        var milestones = CoachProgressBuilder.Milestones(days, S, "en-US");

        // assert
        milestones.Single(m => m.Kind == CoachMilestoneKind.Words1K).AchievedAt.Should().Be(d0 + TimeSpan.FromDays(6));
        milestones.Single(m => m.Kind == CoachMilestoneKind.Words10K).AchievedAt.Should().BeNull();
        milestones.Single(m => m.Kind == CoachMilestoneKind.FiveDayWeek).AchievedAt.Should().NotBeNull();
    }

    [Fact]
    public void WeeklyScoresShouldReturnOneEntryPerWeekNewestLast()
    {
        // arrange
        var days = Enumerable.Range(0, 28)
            .Select(i => Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(27 - i), 300, 9, 150))
            .ToList();

        // act
        var scores = CoachProgressBuilder.WeeklyScores(days, 4, Now, S, "en-US");

        // assert
        scores.Should().HaveCount(4);
        scores[^1].Score.Should().NotBeNull();
        scores.Select(s => s.WeekStart).Should().BeInAscendingOrder();
    }
}
