using ActualChat.UI.Blazor.Components;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.UnitTests.Charts;

public class BarChartTest
{
    [Fact]
    public void BarChartShouldScaleBarsToTheTallestOne()
    {
        // arrange
        using var context = TestBunitContext.New();
        var items = new[] { new ChartItem("Mon", 120), new ChartItem("Tue", 60), new ChartItem("Wed", 0) };

        // act
        var cut = context.Render<BarChart>(p => p.Add(x => x.Items, items));

        // assert
        var bars = cut.FindAll(".c-bar");
        bars.Count.Should().Be(3);
        bars[0].GetAttribute("style").Should().Contain("height: 100%");
        bars[1].GetAttribute("style").Should().Contain("height: 50%");
        bars[2].GetAttribute("style").Should().Contain("height: 0%");
        bars[0].GetAttribute("title").Should().Be("120");
    }

    [Fact]
    public void BarChartShouldDrawTheReferenceLineAtItsShareOfTheTallestBar()
    {
        // arrange
        using var context = TestBunitContext.New();
        var items = new[] { new ChartItem("Mon", 200), new ChartItem("Tue", 100) };

        // act
        var cut = context.Render<BarChart>(p => p.Add(x => x.Items, items).Add(x => x.ReferenceValue, 150));

        // assert
        cut.Find(".c-reference").GetAttribute("style").Should().Contain("bottom: 75%");
    }

    [Fact]
    public void BarChartShouldDrawNoReferenceLineWithoutAValue()
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var cut = context.Render<BarChart>(p => p.Add(x => x.Items, [new ChartItem("Mon", 1)]));

        // assert
        cut.FindAll(".c-reference").Should().BeEmpty();
    }
}
