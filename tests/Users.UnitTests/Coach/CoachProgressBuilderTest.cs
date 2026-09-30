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
