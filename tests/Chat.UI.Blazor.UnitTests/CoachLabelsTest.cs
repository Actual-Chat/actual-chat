using ActualChat.UI.Blazor.App.Components;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CoachLabelsTest
{
    private static CoachLabels NewLabels()
        => new (new TestStringLocalizer(new() {
            ["Coach_NoData"] = "no data",
            ["Coach_PercentOfSpeech_Format"] = "{0}% of speech",
        }));

    [Theory]
    [InlineData("ru", "Русский")]
    [InlineData("en", "English")]
    [InlineData("xx", "xx")]
    public void LanguageNameShouldBeTheLanguagesOwnWord(string iso, string expected)
    {
        // arrange
        var labels = NewLabels();

        // act
        var name = labels.LanguageName(iso);

        // assert
        name.Should().Be(expected);
    }

    [Fact]
    public void ValueShouldSayNoDataForACountWithoutATaggedShare()
    {
        // arrange
        var labels = NewLabels();
        var metric = new CoachMetric(CoachMetricKind.Fillers, 0, null, CoachBand.None, ApiArray<CoachChip>.Empty);

        // act
        var value = labels.Value(metric);

        // assert
        value.Should().Be("no data", "a zero count with no tagged words is absence of data, not a clean sheet");
    }

    [Fact]
    public void ValueAndRateShouldSplitACountedMetricForTheRowLayout()
    {
        // arrange
        var labels = NewLabels();
        var metric = new CoachMetric(CoachMetricKind.Fillers, 3, 0.05, CoachBand.Medium, ApiArray<CoachChip>.Empty);

        // act
        var value = labels.Value(metric);
        var rate = labels.Rate(metric);

        // assert
        value.Should().Be("3", "the row title carries the count");
        rate.Should().Be("5% of speech", "the right side carries the share");
    }

    private static CoachLabels NewProgressLabels()
        => new (new TestStringLocalizer(new() {
            ["Coach_PercentOfSpeech_Format"] = "{0}% of speech",
            ["Coach_Wpm_Format"] = "{0} wpm",
            ["Coach_NotEnoughSpeech"] = "not enough speech",
            ["Coach_DeltaSame"] = "about the same",
            ["Coach_PercentagePoints_Format"] = "{0} pp",
            ["Coach_NothingToCompare"] = "no earlier week",
        }));

    [Fact]
    public void DeltaValueAndBadgeShouldFormatPerKind()
    {
        // arrange
        var l = NewProgressLabels();
        var fillers = new CoachWeekDelta(CoachMetricKind.Fillers, 0.07, 0.04, CoachBand.Medium, true);
        var pace = new CoachWeekDelta(CoachMetricKind.Pace, 95, 118, CoachBand.Good, true);
        var monologue = new CoachWeekDelta(CoachMetricKind.Monologue, 190, 140, CoachBand.High, true);
        var same = new CoachWeekDelta(CoachMetricKind.WeakWords, 0.03, 0.03, CoachBand.Good, null);
        var unknown = new CoachWeekDelta(CoachMetricKind.Fillers, null, 0.04, CoachBand.Medium, null);

        // act & assert
        l.DeltaValue(fillers).Should().Be("7% → 4% of speech");
        l.DeltaBadge(fillers).Should().Be("↑ 43%");
        l.DeltaValue(pace).Should().Be("95 → 118 wpm");
        l.DeltaBadge(pace).Should().Be("↑ 23");
        l.DeltaValue(monologue).Should().Be("3:10 → 2:20");
        l.DeltaBadge(monologue).Should().Be("↑ 0:50");
        l.DeltaBadge(same).Should().Be("→ about the same");
        l.DeltaValue(unknown)
            .Should().Be("4% of speech", "the current value stands alone until there is an earlier week");
        l.DeltaBadge(unknown).Should().BeEmpty("there is no earlier week to be better or worse than");
    }

    [Theory]
    [InlineData(0.07, 0.04, true, "↑ 3 pp")]
    [InlineData(0d, 0.02, false, "↓ 2 pp")]
    [InlineData(0.031, 0.027, null, "→ about the same")]
    [InlineData(0.03, 0.03, null, "→ about the same")]
    [InlineData(null, 0.04, null, "no earlier week")]
    [InlineData(0.04, null, null, "not enough speech")]
    public void RecentChangesShouldUsePercentagePointsAndPreserveMissingData(
        double? previous, double? current, bool? isBetter, string expected)
    {
        var labels = NewProgressLabels();
        var delta = new CoachWeekDelta(CoachMetricKind.Fillers, previous, current, CoachBand.Good, isBetter);

        var badge = labels.RecentDeltaBadge(delta);

        badge.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, 0d, false)]
    [InlineData(0d, null, false)]
    [InlineData(0d, 0d, true)]
    [InlineData(0.04, 0.07, true)]
    public void ComparisonEligibilityShouldRequireBothPeriodsButAllowZeroRates(
        double? previous, double? current, bool expected)
    {
        // arrange
        var delta = new CoachWeekDelta(CoachMetricKind.Fillers, previous, current, CoachBand.Good, null);

        // act
        var hasComparison = CoachLabels.HasComparison(delta);

        // assert
        hasComparison.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.07, 0.04, true, "improving")]
    [InlineData(0.04, 0.07, false, "worsening")]
    [InlineData(0.03, 0.03, null, "stable")]
    [InlineData(null, 0.04, null, "text-03")]
    [InlineData(0.04, null, null, "text-03")]
    public void DeltaColorsShouldDistinguishProgressFromMissingData(
        double? previous, double? current, bool? isBetter, string expected)
    {
        // arrange
        var delta = new CoachWeekDelta(CoachMetricKind.Fillers, previous, current, CoachBand.Good, isBetter);

        // act
        var cls = CoachLabels.DeltaClass(delta);

        // assert
        cls.Should().Be(expected);
    }

    [Fact]
    public void RecentPaceShouldKeepANeutralRangeChangeNeutral()
    {
        var labels = NewProgressLabels();
        var delta = new CoachWeekDelta(CoachMetricKind.Pace, 110, 130, CoachBand.Good, null);

        var badge = labels.RecentDeltaBadge(delta);
        var value = labels.RecentDeltaValue(delta);

        badge.Should().Be("→ about the same");
        value.Should().Be("130 wpm");
    }

    [Fact]
    public void RecentRateShouldPreserveTenthsOfAPercent()
    {
        var labels = NewProgressLabels();
        var delta = new CoachWeekDelta(CoachMetricKind.WeakWords, 0.031, 0.027, CoachBand.Good, true);

        var value = labels.RecentDeltaValue(delta);

        value.Should().Be("2.7% of speech");
    }

    [Fact]
    public void DeltaValueShouldSayNotEnoughSpeechOnlyWhenThisWeekHasNone()
    {
        // arrange
        var l = NewProgressLabels();
        var quiet = new CoachWeekDelta(CoachMetricKind.Fillers, 0.05, null, CoachBand.None, null);

        // act & assert
        l.DeltaValue(quiet).Should().Be("not enough speech");
        l.DeltaBadge(quiet).Should().BeEmpty("an unavailable current value is not an unchanged comparison");
    }

    [Fact]
    public void ValueShouldShowTheLongestMonologueAsAClock()
    {
        // arrange
        var labels = NewLabels();
        var metric = new CoachMetric(CoachMetricKind.Monologue, 237.9, null, CoachBand.High, ApiArray<CoachChip>.Empty);

        // act
        var value = labels.Value(metric);

        // assert
        value.Should().Be("3:57", "Recent shows the same monologue as a clock");
    }

    [Fact]
    public void ExplainShouldNameTheLanguageAndItsRange()
    {
        // arrange
        var l = new CoachLabels(new TestStringLocalizer(new() {
            ["Coach_ExplainPace_Format"] = "Words per minute while you speak. {0} to {1} is easy to follow in {2}.",
            ["Coach_ExplainFillers_Format"] = "Words that fill a gap: {0}. Under {1}% sounds natural.",
            ["Coach_BandABitHigh"] = "a bit high",
            ["Coach_BandABitMuch"] = "a bit much",
            ["Coach_PaceComfortableWord"] = "comfortable",
            ["Coach_BandHigh"] = "High",
            ["Coach_BandShort"] = "Short",
            ["Coach_BandBalanced"] = "Balanced",
        }));
        var summary = CoachSummary.None with { PaceSlow = 100, PaceFast = 140, FillerGood = 0.03 };

        // act & assert
        l.Explain(CoachMetricKind.Pace, "ru", summary)
            .Should().Be("Words per minute while you speak. 100 to 140 is easy to follow in Russian.");
        l.Explain(CoachMetricKind.Fillers, "ru", summary).Should().StartWith("Words that fill a gap");
        l.BandWord(CoachMetricKind.Fillers, CoachBand.Medium).Should().Be("a bit high");
        l.BandWord(CoachMetricKind.TurnTaking, CoachBand.High).Should().Be("a bit much");
        l.BandWord(CoachMetricKind.Pace, CoachBand.Good).Should().Be("comfortable");
        l.BandWord(CoachMetricKind.Fillers, CoachBand.High).Should().Be("high", "every hint starts lowercase");
        l.BandWord(CoachMetricKind.SentenceLength, CoachBand.Low).Should().Be("short");
        l.BandWord(CoachMetricKind.TurnTaking, CoachBand.Good).Should().Be("balanced");
    }

    [Fact]
    public void WindowShouldLabelEveryPeriodDifferently()
    {
        // arrange
        var l = new CoachLabels(new TestStringLocalizer(new() {
            ["Coach_WindowToday"] = "Today",
            ["Coach_WindowWeek"] = "Week",
            ["Coach_WindowMonth"] = "Month",
            ["Coach_WindowAllTime"] = "All time",
            ["Coach_WindowDays7"] = "7 days",
            ["Coach_WindowDays30"] = "30 days",
        }));

        // act
        var labels = new[] { CoachWindow.Days7, CoachWindow.Days30, CoachWindow.AllTime }.Select(l.Window).ToList();

        // assert
        labels.Should().Equal("7 days", "30 days", "All time");
    }
}
