using ActualChat.UI.Blazor.Components;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.UnitTests.Charts;

public class DonutChartTest
{
    [Fact]
    public void DonutChartShouldRenderOneSlicePerPositiveItemWithSharesSummingToOneHundred()
    {
        // arrange
        using var context = TestBunitContext.New();
        var items = new[] { new ChartItem("a", 30, "x"), new ChartItem("b", 10, "y"), new ChartItem("c", 0, "z") };

        // act
        var cut = context.Render<DonutChart>(p => p.Add(x => x.Items, items));

        // assert
        var slices = cut.FindAll("circle.c-slice");
        slices.Count.Should().Be(2, "a zero item has no slice");
        slices[0].GetAttribute("stroke-dasharray").Should().Be("75.00 25.00");
        slices[1].GetAttribute("stroke-dasharray").Should().Be("25.00 75.00");
        slices[1].GetAttribute("stroke-dashoffset").Should().Be("-75.00", "the second slice starts where the first ends");
        cut.FindAll("li").Count.Should().Be(3, "the legend lists every item");
    }

    [Fact]
    public void DonutChartShouldRenderNoSlicesForAllZeroItems()
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var cut = context.Render<DonutChart>(p => p.Add(x => x.Items, [new ChartItem("a", 0)]));

        // assert
        cut.FindAll("circle.c-slice").Should().BeEmpty();
    }
}
