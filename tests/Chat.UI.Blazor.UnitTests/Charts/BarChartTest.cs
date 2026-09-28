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
}
