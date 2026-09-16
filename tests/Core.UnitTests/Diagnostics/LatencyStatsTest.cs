using ActualChat.Diagnostics;

namespace ActualChat.Core.UnitTests.Diagnostics;

public class LatencyStatsTest
{
    [Fact]
    public void EmptyStatsShouldSayZero()
    {
        var stats = new LatencyStats();

        stats.Count.Should().Be(0);
        stats.First.Should().BeNull();
        stats.Max.Should().BeNull();
        stats.Median.Should().BeNull();
        stats.ToString().Should().Be("n=0");
    }

    [Fact]
    public void StatsShouldTrackFirstMaxAndMedian()
    {
        var stats = new LatencyStats();
        foreach (var seconds in new[] { 0.9, 1.4, 0.7, 1.1 })
            stats.Add(TimeSpan.FromSeconds(seconds));

        stats.Count.Should().Be(4);
        stats.First.Should().Be(TimeSpan.FromSeconds(0.9));
        stats.Max.Should().Be(TimeSpan.FromSeconds(1.4));
        // Even count: the upper of the two middle values, so the number is one that was seen
        stats.Median.Should().Be(TimeSpan.FromSeconds(1.1));
        stats.ToString().Should().Be("p50 1.1s max 1.4s (n=4)");
    }

    [Fact]
    public void SingleValueShouldBeItsOwnMedian()
    {
        var stats = new LatencyStats();
        stats.Add(TimeSpan.FromSeconds(1.6));

        stats.ToString().Should().Be("p50 1.6s max 1.6s (n=1)");
    }
}
