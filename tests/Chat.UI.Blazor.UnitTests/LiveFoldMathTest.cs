using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class LiveFoldMathTest
{
    private static readonly Moment T0 = new(new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc));
    private static readonly TimeSpan Lag = TimeSpan.FromMinutes(3);

    [Fact]
    public void RipeFoldAdvancesBoundary()
    {
        // arrange
        var pending = new List<PendingFold> { new(21, T0) };

        // act
        var result = LiveFoldMath.Advance(10, pending, T0 + Lag, Lag, null);

        // assert
        result.BoundaryLid.Should().Be(21);
        result.Pending.Should().BeEmpty();
        result.NextWakeAt.Should().BeNull();
    }

    [Fact]
    public void UnripeFoldStaysPendingAndSchedulesWake()
    {
        // arrange
        var pending = new List<PendingFold> { new(21, T0) };

        // act
        var result = LiveFoldMath.Advance(10, pending, T0 + TimeSpan.FromMinutes(1), Lag, null);

        // assert
        result.BoundaryLid.Should().Be(10);
        result.Pending.Should().ContainSingle().Which.FoldEndLid.Should().Be(21);
        result.NextWakeAt.Should().Be(T0 + Lag);
    }

    [Fact]
    public void ViewportClampHoldsBoundaryAndKeepsFoldPending()
    {
        // arrange - Entry 15 is visible - the boundary must not cross it even though the fold is ripe
        var pending = new List<PendingFold> { new(21, T0) };

        // act
        var result = LiveFoldMath.Advance(10, pending, T0 + Lag, Lag, 15);

        // assert
        result.BoundaryLid.Should().Be(15);
        result.Pending.Should().ContainSingle().Which.FoldEndLid.Should().Be(21);
        result.NextWakeAt.Should().BeNull(); // ripe - only visibility holds it, no timer needed
    }

    [Fact]
    public void BoundaryIsMonotonic()
    {
        // arrange - A visible lid below the current boundary (viewer expanded the fold) must not move it back
        var pending = new List<PendingFold>();

        // act
        var result = LiveFoldMath.Advance(20, pending, T0, Lag, 5);

        // assert
        result.BoundaryLid.Should().Be(20);
    }

    [Fact]
    public void MaxRipeFoldWinsAndEarliestUnripeSchedulesWake()
    {
        // arrange
        var pending = new List<PendingFold>
        {
            new(15, T0),
            new(21, T0 + TimeSpan.FromSeconds(30)),
            new(30, T0 + TimeSpan.FromMinutes(2))
        };

        // act
        var result = LiveFoldMath.Advance(10, pending, T0 + Lag + TimeSpan.FromMinutes(1), Lag, null);

        // assert
        result.BoundaryLid.Should().Be(21);
        result.Pending.Should().ContainSingle().Which.FoldEndLid.Should().Be(30);
        result.NextWakeAt.Should().Be(T0 + TimeSpan.FromMinutes(2) + Lag);
    }
}
