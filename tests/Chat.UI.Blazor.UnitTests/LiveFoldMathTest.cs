using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public sealed class LiveFoldMathTest
{
    [Fact]
    public void AdvanceShouldMoveToViewportTop()
        => LiveFoldMath.Advance(10, 15).Should().Be(15);

    [Fact]
    public void AdvanceShouldFoldUnsummarizedRowsAboveViewport()
        => LiveFoldMath.Advance(10, 500).Should().Be(500);

    [Fact]
    public void AdvanceShouldNotRetreatWhenViewportIsBelowBoundary()
        => LiveFoldMath.Advance(20, 5).Should().Be(20);

    [Fact]
    public void AdvanceShouldHoldBoundaryForNullViewport()
        => LiveFoldMath.Advance(20, null).Should().Be(20);

    [Fact]
    public void AdvanceShouldBeStableForEqualViewport()
        => LiveFoldMath.Advance(20, 20).Should().Be(20);
}
