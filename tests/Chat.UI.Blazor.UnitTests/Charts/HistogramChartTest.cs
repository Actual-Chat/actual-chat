using ActualChat.UI.Blazor.Components;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.UnitTests.Charts;

public sealed class HistogramChartTest
{
    [Fact]
    public void DistributionShouldUsePaceForPositionAndSpeechAmountForHeight()
    {
        // arrange
        using var context = TestBunitContext.New();
        var bins = new Dictionary<int, long> { [20] = 16_200, [30] = 10_100 };

        // act
        var cut = context.Render<HistogramChart>(p => p.Add(x => x.Bins, bins)
            .Add(x => x.TargetRange, new Range<double>(100, 140))
            .Add(x => x.BinHint, (bin, value) => $"{bin * 5}–<{bin * 5 + 5} wpm · {value} ms")
            .Add(x => x.ValueLabel, x => $"{x / 1_000:0.#} s").Add(x => x.XUnit, "wpm"));

        // assert
        cut.Find(".c-target").GetAttribute("style").Should().Be("left: 20%; width: 32%");
        cut.Find("[data-bin='20']").GetAttribute("style").Should().Contain("left: 20%")
            .And.Contain("width: 4%; height: 100%");
        cut.Find("[data-bin='30']").GetAttribute("style").Should().Contain("left: 60%")
            .And.Contain("height: 62.3457%");
        cut.Find("[data-bin='20']").GetAttribute("aria-label").Should().Contain("100–<105 wpm");
        cut.Find(".c-bounds").TextContent.Should().Contain("100–140");
        cut.Find(".c-y-axis").TextContent.Should().Contain("16.2 s");
        cut.FindAll(".c-bin").Should().HaveCount(2);
    }

    [Fact]
    public void PercentageScaleShouldUseTotalSpeechRatherThanTheLargestBin()
    {
        // arrange
        using var context = TestBunitContext.New();
        var bins = new Dictionary<int, long> { [20] = 16_200, [30] = 10_100 };
        var total = bins.Values.Sum();

        // act
        var cut = context.Render<HistogramChart>(p => p.Add(x => x.Bins, bins)
            .Add(x => x.MaximumValue, total)
            .Add(x => x.ValueLabel, x => (100 * x / total).ToString("0.#", null) + "%"));

        // assert
        cut.FindAll(".c-y-axis span").Select(x => x.TextContent).Should().Equal("100%", "50%", "0%");
        cut.Find("[data-bin=\'20\']").GetAttribute("style").Should().Contain("height: 61.597%");
        cut.Find("[data-bin=\'30\']").GetAttribute("style").Should().Contain("height: 38.403%");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(99)]
    public void InvalidVerticalMaximumShouldBeRejected(double maximum)
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var act = () => context.Render<HistogramChart>(p => p.Add(x => x.MaximumValue, maximum)
            .Add(x => x.Bins, new Dictionary<int, long> { [20] = 100 }));

        // assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TargetShouldRemainVisibleWhenAllSpeechIsOutsideIt()
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var cut = context.Render<HistogramChart>(p => p.Add(x => x.Bins, new Dictionary<int, long> { [60] = 1000 })
            .Add(x => x.TargetRange, new Range<double>(100, 140)));

        // assert
        cut.FindAll(".c-target").Should().ContainSingle();
        cut.Find(".c-bounds").TextContent.Should().Contain("100–140");
        cut.FindAll(".c-bin").Should().ContainSingle();
    }

    [Fact]
    public void EmptyDistributionShouldNotInventSpeech()
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var cut = context.Render<HistogramChart>(p => p.Add(x => x.TargetRange, new Range<double>(100, 140)));

        // assert
        cut.FindAll(".c-bin").Should().BeEmpty();
        cut.FindAll(".c-target").Should().ContainSingle();
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(1, 0)]
    [InlineData(1, -100)]
    public void InvalidBinsShouldBeRejected(int bin, long duration)
    {
        // arrange
        using var context = TestBunitContext.New();
        var bins = new Dictionary<int, long> { [bin] = duration };

        // act
        var act = () => context.Render<HistogramChart>(p => p.Add(x => x.Bins, bins));

        // assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
