using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public sealed class LiveFoldMathTest
{
    private const long NoFloor = long.MaxValue;

    [Fact]
    public void AdvanceShouldMoveToViewportTop()
        => LiveFoldMath.Advance(10, 15, NoFloor, NoFloor).Should().Be(15);

    [Fact]
    public void AdvanceShouldNotRetreatWhenViewportIsBelowBoundary()
        => LiveFoldMath.Advance(20, 5, NoFloor, NoFloor).Should().Be(20);

    [Fact]
    public void AdvanceShouldHoldWhenNothingOfTheBlockIsVisible()
        => LiveFoldMath.Advance(20, 0, NoFloor, NoFloor).Should().Be(20);

    [Fact]
    public void AdvanceShouldBeStableForEqualViewport()
        => LiveFoldMath.Advance(20, 20, NoFloor, NoFloor).Should().Be(20);

    [Fact]
    public void AdvanceShouldStopAtTheStreamingFloor()
        => LiveFoldMath.Advance(10, 30, 20, NoFloor).Should().Be(20);

    [Fact]
    public void AdvanceShouldStopAtTheTailFloor()
        => LiveFoldMath.Advance(10, 30, NoFloor, 20).Should().Be(20);

    [Fact]
    public void AdvanceShouldNeverFoldAboveTheViewportTopWhenTheTailFloorRises()
        // The floor climbs with every new message; it must not drag the fold over a row that's on
        // screen, so the viewport top wins even though the floor is far above it.
        => LiveFoldMath.Advance(10, 12, NoFloor, 30).Should().Be(12);

    [Fact]
    public void AdvanceShouldNeverFoldAboveTheViewportTopWhenTheStreamingFloorLapses()
        // The transcript closed, so its floor is gone - the fold resumes at the viewport top rather
        // than jumping to wherever the boundary would otherwise have run.
        => LiveFoldMath.Advance(10, 12, NoFloor, NoFloor).Should().Be(12);
}
