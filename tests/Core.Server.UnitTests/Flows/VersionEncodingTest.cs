using ActualLab.Versioning;

namespace ActualChat.Core.Server.UnitTests.Flows;

public class VersionEncodingTest
{
    // FlowBackend.List derives FlowSummary.UpdatedAt and the stuck cutoff from DbFlow.Version,
    // assuming Version == clock-based epoch ticks. This guards that assumption.
    [Fact]
    public void VersionIsEpochTicks()
    {
        var clock = MomentClockSet.Default.SystemClock;
        var generator = new ClockBasedVersionGenerator(clock);
        var now = clock.Now;
        var version = generator.NextVersion(0);
        Math.Abs(version - now.EpochOffset.Ticks).Should().BeLessThan(TimeSpan.FromSeconds(5).Ticks);
    }
}
