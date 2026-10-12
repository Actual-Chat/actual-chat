using ActualChat.UI.Blazor.Components;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests.Charts;

public sealed class LineChartTest
{
    [Fact]
    public void TargetAndReferenceShouldUseTheSameScaleAsTheMeasurements()
    {
        // arrange
        using var context = TestBunitContext.New();
        var items = new[] { new ChartItem("Mon", 154), new ChartItem("Tue", 160) };

        // act
        var cut = context.Render<LineChart>(p => p.Add(x => x.Items, items)
            .Add(x => x.TargetRange, new Range<double>(130, 170))
            .Add(x => x.ReferenceValue, 120).Add(x => x.ScaleStep, 50));

        // assert
        cut.Find(".c-target").GetAttribute("style").Should().Contain("top: 30%; height: 40%");
        cut.Find(".c-reference").GetAttribute("style").Should().Contain("top: 80%");
        cut.Find(".c-target-bound.c-upper").TextContent.Trim().Should().Be("170");
        cut.Find(".c-target-bound.c-upper").GetAttribute("style").Should().Be("top: 30%");
        cut.Find(".c-target-bound.c-lower").TextContent.Trim().Should().Be("130");
        cut.Find(".c-target-bound.c-lower").GetAttribute("style").Should().Be("top: 70%");
        cut.Find(".c-line").GetAttribute("y1").Should().Be("46");
        cut.Find(".c-line").GetAttribute("y2").Should().Be("40");
        cut.FindAll(".c-axis span").Select(x => x.TextContent).Should().Equal("200", "150", "100");
    }

    [Fact]
    public void TargetShouldStayVisibleWhenAllMeasurementsAreOutsideIt()
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var cut = context.Render<LineChart>(p => p.Add(x => x.Items, [new ChartItem("Mon", 300)])
            .Add(x => x.TargetRange, new Range<double>(130, 170)).Add(x => x.ScaleStep, 50));

        // assert
        cut.FindAll(".c-target").Should().ContainSingle();
        cut.FindAll(".c-axis span").Select(x => x.TextContent).Should().Equal("350", "225", "100");
        cut.FindAll(".c-line").Should().BeEmpty();
        cut.FindAll(".c-point").Should().ContainSingle();
    }

    [Fact]
    public void NarrowTargetBandShouldRetainBothBoundsInOneLabel()
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var cut = context.Render<LineChart>(p => p.Add(x => x.Items, [new ChartItem("Mon", 2000)])
            .Add(x => x.TargetRange, new Range<double>(130, 170)).Add(x => x.ScaleStep, 50));

        // assert
        cut.Find(".c-target-bound.c-pair").TextContent.Trim().Should().Be("130–170");
        cut.FindAll(".c-target-tick").Should().HaveCount(2);
        cut.FindAll(".c-target-bound").Should().ContainSingle();
    }

    [Fact]
    public void ZeroReferenceAndZeroMeasurementShouldNotDisappear()
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var cut = context.Render<LineChart>(p => p.Add(x => x.Items, [new ChartItem("Mon", 0)])
            .Add(x => x.ReferenceValue, 0).Add(x => x.TargetRange, new Range<double>(130, 170))
            .Add(x => x.ScaleStep, 50));

        // assert
        cut.Find(".c-reference").GetAttribute("style").Should().Contain("top: 100%");
        cut.Find(".c-point").GetAttribute("style").Should().Contain("top: 100%");
        cut.Find(".sr-only").TextContent.Should().Be("Mon · 0");
        cut.FindAll(".c-axis span").Select(x => x.TextContent).Should().Equal("200", "100", "0");
    }

    [Fact]
    public void InactiveDaysShouldBreakTheLineAndRetainTheirHint()
    {
        // arrange
        using var context = TestBunitContext.New();
        var items = new[] {
            new ChartItem("1 Oct", 140),
            new ChartItem("4 Oct", 160, Hint: "4 Oct · 160 · 2 inactive days", HasGap: true),
            new ChartItem("5 Oct", 150),
        };

        // act
        var cut = context.Render<LineChart>(p => p.Add(x => x.Items, items));

        // assert
        cut.FindAll(".c-line").Should().ContainSingle();
        cut.Find(".c-line").GetAttribute("x1").Should().Be("50");
        cut.FindAll(".c-gap").Should().ContainSingle();
        cut.FindAll(".sr-only").Select(x => x.TextContent).Should().Contain("4 Oct · 160 · 2 inactive days");
    }

    [Fact]
    public async Task PointSelectionShouldReturnTheOriginalItemWithAnAccessibleHint()
    {
        // arrange
        using var context = TestBunitContext.New();
        var item = new ChartItem("Mon", 154, Hint: "Monday · 154 words/min");
        ChartItem? selected = null;

        // act
        var cut = context.Render<LineChart>(p => p.Add(x => x.Items, [item])
            .Add(x => x.ItemClick, EventCallback.Factory.Create<ChartItem>(this, x => selected = x)));
        await cut.InvokeAsync(() => cut.Find(".btn-chart-point").Click());

        // assert
        selected.Should().BeSameAs(item);
        cut.Find(".btn-chart-point").GetAttribute("aria-label").Should().Be(item.Hint);
        cut.FindAll(".sr-only").Should().BeEmpty();
    }

    [Fact]
    public void EmptyHistoryShouldNotInventPointsOrReferences()
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var cut = context.Render<LineChart>();

        // assert
        cut.FindAll(".c-point, .btn-chart-point, .c-reference, .c-target, .c-line").Should().BeEmpty();
        cut.Markup.Should().NotContain("NaN").And.NotContain("Infinity");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteMeasurementsShouldBeRejected(double value)
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var render = () => context.Render<LineChart>(p => p.Add(x => x.Items, [new ChartItem("Mon", value)]));

        // assert
        render.Should().Throw<ArgumentOutOfRangeException>();
    }
}
