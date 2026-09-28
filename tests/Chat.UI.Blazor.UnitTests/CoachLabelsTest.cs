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
    public void ValueShouldShowTheCountAndShareOnceWordsWereTagged()
    {
        // arrange
        var labels = NewLabels();
        var metric = new CoachMetric(CoachMetricKind.Fillers, 3, 0.05, CoachBand.Medium, ApiArray<CoachChip>.Empty);

        // act
        var value = labels.Value(metric);

        // assert
        value.Should().Be("3 · 5% of speech");
    }
}
