using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachFocusTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly CoachScoringSettings S = new();

    private static CoachSummary Summary(params (CoachMetricKind Kind, CoachBand Band)[] metrics)
        => new (CoachWindow.Days7, 60, null, 500, 5, 5,
            metrics.Select(m => new CoachMetric(m.Kind, 1, 1, m.Band, ApiArray<CoachChip>.Empty)).ToApiArray());

    [Fact]
    public void PickShouldReturnTheWorstHeadlineSkillInOrder()
    {
        // arrange
        var summary = Summary((CoachMetricKind.Fillers, CoachBand.Medium), (CoachMetricKind.Pace, CoachBand.High),
            (CoachMetricKind.TurnTaking, CoachBand.High), (CoachMetricKind.Monologue, CoachBand.Good));

        // act & assert
        CoachFocus.Pick(summary, CoachLanguageLevel.Native, S).Should().Be(CoachMetricKind.Pace, "first High in order");
        CoachFocus.Pick(summary, CoachLanguageLevel.Learning, S).Should().Be(CoachMetricKind.Pace,
            "weak words, vocabulary and sentence length have no data");
    }

    [Fact]
    public void PickShouldReturnNullBelowTheWordFloor()
    {
        var summary = Summary((CoachMetricKind.Fillers, CoachBand.High)) with { Words = 10 };
        CoachFocus.Pick(summary, CoachLanguageLevel.Native, S).Should().BeNull();
    }

    [Fact]
    public void PickShouldPreferTheFirstGoodSkillWhenAllAreGood()
    {
        var summary = Summary((CoachMetricKind.Fillers, CoachBand.Good), (CoachMetricKind.Pace, CoachBand.Good));
        CoachFocus.Pick(summary, CoachLanguageLevel.Native, S).Should().Be(CoachMetricKind.Fillers);
    }
}
