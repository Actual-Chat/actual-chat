using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public sealed class RpcPathChangeTest
{
    [Theory]
    [InlineData(50, 60)]
    [InlineData(50, 120)]
    [InlineData(200, 300)]
    [InlineData(1, 5)]
    public void ShouldIgnoreOrdinaryJitter(double beforeMs, double afterMs)
    {
        // act
        var isChange = Change(beforeMs, afterMs);

        // assert
        isChange.Should().BeFalse(
            because: "every crossing costs a full re-measurement, so noise must not trigger one");
    }

    [Theory]
    [InlineData(80, 400)]
    [InlineData(100, 900)]
    public void ShouldDetectADegradedRoute(double beforeMs, double afterMs)
    {
        // act
        var isChange = Change(beforeMs, afterMs);

        // assert
        isChange.Should().BeTrue(because: "a route that got much worse may have started capping traffic");
    }

    [Theory]
    [InlineData(400, 80)]
    [InlineData(900, 100)]
    public void ShouldDetectAnImprovedRoute(double beforeMs, double afterMs)
    {
        // act
        var isChange = Change(beforeMs, afterMs);

        // assert
        isChange.Should().BeTrue(
            because: "a route that got much better may mean a relay is no longer the best way out");
    }

    [Fact]
    public void ShouldBeSymmetric()
    {
        // act & assert
        Change(100, 500).Should().Be(Change(500, 100),
            because: "the trigger asks whether the route changed, not which way it went");
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(500, 0)]
    [InlineData(-1, 500)]
    public void ShouldRejectUnusableMeasurements(double beforeMs, double afterMs)
    {
        // act
        var isChange = Change(beforeMs, afterMs);

        // assert
        isChange.Should().BeFalse(because: "a non-positive round trip is an artifact, not a fast link");
    }

    // Private methods

    private static bool Change(double beforeMs, double afterMs)
        => RpcEndpointMonitor.IsPathChange(
            TimeSpan.FromMilliseconds(beforeMs),
            TimeSpan.FromMilliseconds(afterMs));
}
