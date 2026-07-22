using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class LiveFoldMathTest
{
    [Fact]
    public void AdvancesToViewportTop()
        => LiveFoldMath.Advance(10, 15).Should().Be(15);

    [Fact]
    public void FoldsUnsummarizedRowsAboveViewport()
        => LiveFoldMath.Advance(10, 500).Should().Be(500);

    [Fact]
    public void IsMonotonic_ViewportBelowBoundaryDoesNotRetreat()
        => LiveFoldMath.Advance(20, 5).Should().Be(20);

    [Fact]
    public void NullViewportHoldsBoundary()
        => LiveFoldMath.Advance(20, null).Should().Be(20);

    [Fact]
    public void EqualViewportIsStable()
        => LiveFoldMath.Advance(20, 20).Should().Be(20);
}
