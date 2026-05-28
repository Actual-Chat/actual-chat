using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class SpeculativeProbeTest
{
    private static readonly VideoSourceKind[] Camera = [VideoSourceKind.Camera];

    private static SpeculativeProbeConfig Cfg()
        => new(
            WindowTicks: 2,
            AckAgeInflationMs: 100,
            HealthyAckAgeMs: 120,
            ShallowQueueBundles: 1.5,
            BaseCooldownTicks: 3,
            CooldownGrowth: 2,
            MaxCooldownTicks: 10);

    // Camera below device max, with room to grow.
    private static LayerCap CameraBelowMax()
    {
        var layers = new LayerCap(deviceCameraCap: 3, screencastCap: 2);
        layers.Reduce(Camera); // 3 -> 2
        return layers;
    }

    [Fact]
    public void FlatAckAgeThroughWindow_CommitsCapClimb()
    {
        var probe = new SpeculativeProbe(Cfg());
        var layers = CameraBelowMax();

        probe.Tick(canGrow: true, ackAgeMs: 50, wireQueueDepth: 0.5, layers, Camera).Should().Be(1);
        probe.IsActive.Should().BeTrue();
        probe.Tick(true, 60, 0.6, layers, Camera).Should().Be(1);
        // Window ends with flat ack-age → commit.
        probe.Tick(true, 55, 0.5, layers, Camera).Should().Be(0);

        layers.CameraLayers.Should().Be(3, "a passed probe commits the cap climb");
        probe.IsActive.Should().BeFalse();
    }

    [Fact]
    public void AckAgeInflation_RevertsWithoutCommitAndBacksOff()
    {
        var probe = new SpeculativeProbe(Cfg());
        var layers = CameraBelowMax();

        probe.Tick(true, 50, 0.5, layers, Camera).Should().Be(1); // baseline ack-age 50
        // Ack-age jumps past baseline + AckAgeInflationMs → probe fails.
        probe.Tick(true, 50 + 150, 0.5, layers, Camera).Should().Be(0);
        layers.CameraLayers.Should().Be(2, "a failed probe does NOT commit");
        probe.IsActive.Should().BeFalse();

        // Now in cooldown — no probe even when healthy.
        probe.Tick(true, 40, 0.4, layers, Camera).Should().Be(0);
    }

    [Fact]
    public void DoesNotProbe_WhenCannotGrow()
    {
        var probe = new SpeculativeProbe(Cfg());
        var layers = CameraBelowMax();
        probe.Tick(canGrow: false, ackAgeMs: 30, wireQueueDepth: 0.3, layers, Camera).Should().Be(0);
        probe.IsActive.Should().BeFalse();
    }

    [Fact]
    public void DoesNotProbe_WhenAckAgeUnhealthyOrQueueDeep()
    {
        var probe = new SpeculativeProbe(Cfg());
        var layers = CameraBelowMax();
        probe.Tick(true, ackAgeMs: 500, wireQueueDepth: 0.3, layers, Camera).Should().Be(0);
        probe.Tick(true, ackAgeMs: 30, wireQueueDepth: 5.0, layers, Camera).Should().Be(0);
        probe.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsWindowAndCooldown()
    {
        var probe = new SpeculativeProbe(Cfg());
        var layers = CameraBelowMax();
        probe.Tick(true, 50, 0.5, layers, Camera).Should().Be(1);
        probe.IsActive.Should().BeTrue();

        probe.Reset();
        probe.IsActive.Should().BeFalse();
        // Can immediately probe again after reset.
        probe.Tick(true, 50, 0.5, layers, Camera).Should().Be(1);
    }
}
