using ActualChat.Bandwidth;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ReprobeGateTest
{
    private const int CalmStreak = 3;
    private static readonly Moment T0 = new(TimeSpan.FromSeconds(0));
    private static readonly BandwidthEstimatorConfig Cfg = new(1_000_000);

    private static Moment At(double sec) => T0 + TimeSpan.FromSeconds(sec);

    private static bool Gate(
        HealthVerdict downlink = HealthVerdict.Good,
        HealthVerdict decoder = HealthVerdict.Good,
        int calmTicks = CalmStreak,
        Moment? downAt = null,
        int probeFailures = 0,
        double nowSec = 100)
        => VideoQualityUI.ShouldReprobe(
            downlink, decoder, calmTicks, CalmStreak, downAt, probeFailures, At(nowSec), Cfg);

    [Fact]
    public void Open_WhenHealthyCalmAndNoCooldown()
        => Gate().Should().BeTrue();

    [Fact]
    public void Closed_WhenDownlinkNotGood()
    {
        Gate(downlink: HealthVerdict.Marginal).Should().BeFalse();
        Gate(downlink: HealthVerdict.Bad).Should().BeFalse();
        Gate(downlink: HealthVerdict.Unknown).Should().BeFalse();
    }

    [Fact]
    public void Closed_WhenDecoderBad()
        => Gate(decoder: HealthVerdict.Bad).Should().BeFalse();

    [Fact]
    public void Closed_BeforeCalmStreak()
        => Gate(calmTicks: CalmStreak - 1).Should().BeFalse();

    [Fact]
    public void Closed_DuringCooldown()
    {
        // probeFailures=0 → base cooldown 5s; only 3s elapsed since the demote.
        Gate(downAt: At(97), nowSec: 100).Should().BeFalse();
    }

    [Fact]
    public void Open_AfterCooldownElapsed()
    {
        // 6s elapsed > 5s base cooldown.
        Gate(downAt: At(94), nowSec: 100).Should().BeTrue();
    }

    [Fact]
    public void CooldownGrowsWithProbeFailures()
    {
        // 6s elapsed. failures=0 → 5s cooldown (open); failures=2 → 5*1.7^2≈14.5s (still closed).
        Gate(downAt: At(94), probeFailures: 0, nowSec: 100).Should().BeTrue();
        Gate(downAt: At(94), probeFailures: 2, nowSec: 100).Should().BeFalse();
    }
}
