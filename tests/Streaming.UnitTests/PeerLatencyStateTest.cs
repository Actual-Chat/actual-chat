using ActualChat.Video;
using static ActualChat.Streaming.StreamLatencyStore;

namespace ActualChat.Streaming.UnitTests;

public class PeerLatencyStateTest
{
    // Pre-warmed factory — simulates a peer that has been reporting for > 10s so
    // RecordLatency doesn't discard samples during warmup.
    private static PeerLatencyState WarmedUp()
        => new(CpuTimestamp.Now - TimeSpan.FromMinutes(1));

    // --- Temporal cap ---

    [Fact]
    public void WarmupDiscardsSamplesAndKeepsDefaults()
    {
        // arrange
        var state = new PeerLatencyState();

        // act
        state.RecordLatency(2000); // would be "slow" if it were recorded

        // assert
        state.MedianLatencyMs.Should().Be(0, "samples are discarded during warmup");
        state.MaxSpatialLayer.Should().Be(int.MaxValue);
        state.MaxTemporalLayer.Should().Be(int.MaxValue);
    }

    [Fact]
    public void StableLowLatencyYieldsFastCaps()
    {
        // arrange: a peer that's seeded a low-latency baseline
        var state = WarmedUp();

        // act: several stable low samples establish baseline and keep median low
        for (var i = 0; i < 6; i++)
            state.RecordLatency(50);

        // assert
        state.IsNetworkFast.Should().BeTrue();
        state.MaxSpatialLayer.Should().Be(2, "fast peer gets top spatial layer");
        state.MaxTemporalLayer.Should().Be(int.MaxValue);
    }

    [Fact]
    public void StableHighBaselineStaysFast_NoCapDrop()
    {
        // Cross-continent peer: stable high RTT, flat baseline. Must NOT be treated as
        // slow — that was the bug in the old absolute-threshold logic.
        // arrange
        var state = WarmedUp();

        // act: 6 samples of a stable 350ms RTT (above old LowLatencyThresholdMs=300ms
        // but flat — no rise above baseline)
        for (var i = 0; i < 6; i++)
            state.RecordLatency(350);

        // assert
        state.BaselineLatencyMs.Should().BeGreaterThan(0, "baseline seeded");
        state.IsNetworkSlow.Should().BeFalse("stable high RTT is not congestion");
        state.MaxSpatialLayer.Should().NotBe(0, "stable high-baseline peer must not be capped at base layer");
        state.MaxTemporalLayer.Should().Be(int.MaxValue, "enhancement layers must not be dropped while baseline is flat");
    }

    [Fact]
    public void RisingLatencyTriggersSlowCaps()
    {
        // arrange: establish a low baseline first
        var state = WarmedUp();
        for (var i = 0; i < 6; i++)
            state.RecordLatency(50);
        state.BaselineLatencyMs.Should().BeLessThan(100);

        // act: latency rises far above baseline (200ms absolute AND 1.3× multiplier)
        for (var i = 0; i < 6; i++)
            state.RecordLatency(1000);

        // assert
        state.IsNetworkSlow.Should().BeTrue();
        state.MaxSpatialLayer.Should().Be(0, "slow peer capped at base layer");
        state.MaxTemporalLayer.Should().Be(0, "temporal enhancement dropped under congestion");
    }

    [Fact]
    public void BorderlineLatencyYieldsMidSpatial()
    {
        // Borderline = median is above baseline + fastMargin (100ms) but below the
        // slow threshold (baseline + 200ms absolute AND baseline × 1.3). Neither
        // IsNetworkSlow nor IsNetworkFast → MID spatial tier.
        // arrange: seed a low baseline at ~100ms first
        var state = WarmedUp();
        for (var i = 0; i < 6; i++)
            state.RecordLatency(100);
        state.BaselineLatencyMs.Should().BeApproximately(100, 5);

        // act: push 3 samples at 250ms so median moves up while baseline EMA lags
        state.RecordLatency(250);
        state.RecordLatency(250);
        state.RecordLatency(250);

        // assert
        state.MedianLatencyMs.Should().Be(250, "queue majority is now 250ms");
        state.IsNetworkSlow.Should().BeFalse("250ms not > baseline(~100)+200 absolute threshold");
        state.IsNetworkFast.Should().BeFalse("250ms not < baseline(~100)+100 fast margin");
        state.MaxSpatialLayer.Should().Be(1, "borderline latency gets mid spatial layer");
        state.MaxTemporalLayer.Should().Be(int.MaxValue, "borderline is not slow — keep enhancement");
    }

    // --- Render hint (Stage 5) ---

    [Fact]
    public void RenderHintIsAppliedEvenDuringWarmup()
    {
        // Render size is known before playback warms up — apply hint immediately.
        // arrange: fresh (cold) state — latency samples would be discarded
        var state = new PeerLatencyState();

        // act
        state.RecordLatency(50, renderQualityLevel: (int)VideoQualityLevel.Low);

        // assert
        state.RenderHintSpatialLayer.Should().Be(0, "Low preset → base layer");
        state.EffectiveMaxSpatial.Should().Be(0, "render hint clamps effective cap even when latency cap is default");
    }

    [Theory]
    [InlineData(VideoQualityLevel.Low, 0)]
    [InlineData(VideoQualityLevel.Medium, 1)]
    [InlineData(VideoQualityLevel.High, 2)]
    [InlineData(VideoQualityLevel.Full, 2)]
    [InlineData(VideoQualityLevel.Ultra, 2)]
    public void RenderHintMapping(VideoQualityLevel level, int expectedSpatial)
    {
        // arrange
        var state = WarmedUp();

        // act
        state.RecordLatency(50, renderQualityLevel: (int)level);

        // assert
        state.RenderHintSpatialLayer.Should().Be(expectedSpatial);
    }

    [Fact]
    public void RenderHintCapsBelowFastLatencyCap()
    {
        // Peer has fast network (latency cap = 2) but only renders sidebar (Low = 0).
        // Effective cap must be 0 — no point sending 720p to a 240px sidebar tile.
        // arrange
        var state = WarmedUp();
        for (var i = 0; i < 6; i++)
            state.RecordLatency(50, renderQualityLevel: (int)VideoQualityLevel.Low);

        // assert
        state.IsNetworkFast.Should().BeTrue();
        state.MaxSpatialLayer.Should().Be(2, "latency-derived cap unaffected by hint");
        state.RenderHintSpatialLayer.Should().Be(0);
        state.EffectiveMaxSpatial.Should().Be(0, "render hint clamps effective cap");
    }

    [Fact]
    public void LatencyCapsBelowRenderHint()
    {
        // Fullscreen consumer (High = 2) on congested link (slow = 0).
        // Effective cap follows the tighter one: 0.
        // arrange
        var state = WarmedUp();
        // seed low baseline
        for (var i = 0; i < 6; i++)
            state.RecordLatency(50, renderQualityLevel: (int)VideoQualityLevel.High);
        // then spike
        for (var i = 0; i < 6; i++)
            state.RecordLatency(1000, renderQualityLevel: (int)VideoQualityLevel.High);

        // assert
        state.IsNetworkSlow.Should().BeTrue();
        state.MaxSpatialLayer.Should().Be(0, "congestion caps to base layer");
        state.RenderHintSpatialLayer.Should().Be(2, "High render hint retained");
        state.EffectiveMaxSpatial.Should().Be(0, "latency cap wins");
    }

    [Fact]
    public void RenderHintUnsetMeansNoHintCap()
    {
        // renderQualityLevel = -1 must not clamp anything.
        // arrange
        var state = WarmedUp();

        // act: several reports without render hint
        for (var i = 0; i < 6; i++)
            state.RecordLatency(50); // default renderQualityLevel = -1

        // assert
        state.RenderHintSpatialLayer.Should().Be(int.MaxValue, "default = no cap");
        state.EffectiveMaxSpatial.Should().Be(2, "latency cap alone applies");
    }

    // --- Egress fallback (Stage 6) ---

    [Fact]
    public void DecrementEgressFallback_FromUncapped_DropsToOne()
    {
        // No latency / render caps — effective starts at int.MaxValue. Decrement
        // treats that as "assume 2" and drops to 1.
        // arrange
        var state = WarmedUp();

        // act
        var floor = state.DecrementEgressFallback();

        // assert
        floor.Should().Be(1);
        state.EgressFallbackSpatialLayer.Should().Be(1);
        state.EffectiveMaxSpatial.Should().Be(1);
    }

    [Fact]
    public void DecrementEgressFallback_CascadesToZero()
    {
        // arrange
        var state = WarmedUp();
        state.DecrementEgressFallback(); // → 1

        // act
        var floor = state.DecrementEgressFallback();

        // assert
        floor.Should().Be(0);
        state.EgressFallbackSpatialLayer.Should().Be(0);
    }

    [Fact]
    public void DecrementEgressFallback_SaturatesAtZero()
    {
        // arrange
        var state = WarmedUp();
        state.DecrementEgressFallback();
        state.DecrementEgressFallback();

        // act
        var floor = state.DecrementEgressFallback();

        // assert
        floor.Should().Be(0, "already at floor — cannot go below 0");
    }

    [Fact]
    public void RestoreEgressFallback_ClearsFloor()
    {
        // arrange
        var state = WarmedUp();
        state.DecrementEgressFallback();
        state.EgressFallbackSpatialLayer.Should().Be(1);

        // act
        state.RestoreEgressFallback();

        // assert
        state.EgressFallbackSpatialLayer.Should().Be(int.MaxValue);
        state.EffectiveMaxSpatial.Should().Be(int.MaxValue, "no other caps set");
    }

    [Fact]
    public void EffectiveMaxSpatial_TakesTightestOfThreeCaps()
    {
        // Latency says 2 (fast), render hint says 1 (Medium), egress fallback says 0.
        // Effective must be 0.
        // arrange
        var state = WarmedUp();
        for (var i = 0; i < 6; i++)
            state.RecordLatency(50, renderQualityLevel: (int)VideoQualityLevel.Medium);
        state.MaxSpatialLayer.Should().Be(2);
        state.RenderHintSpatialLayer.Should().Be(1);

        // act
        state.DecrementEgressFallback(); // from effective min(2,1)=1 → 0

        // assert
        state.EgressFallbackSpatialLayer.Should().Be(0);
        state.EffectiveMaxSpatial.Should().Be(0, "tightest of three wins");
    }

    [Fact]
    public void DecrementClampsBelowCurrentEffective_NotBelowMaxSpatial()
    {
        // Latency cap = 1 (borderline). Decrement should go from effective=1 to 0.
        // arrange
        var state = WarmedUp();
        // establish borderline: baseline ~100, median rises to 250
        for (var i = 0; i < 6; i++)
            state.RecordLatency(100);
        state.RecordLatency(250);
        state.RecordLatency(250);
        state.RecordLatency(250);
        state.MaxSpatialLayer.Should().Be(1, "borderline → mid");

        // act
        state.DecrementEgressFallback();

        // assert
        state.EgressFallbackSpatialLayer.Should().Be(0, "one below current effective");
        state.EffectiveMaxSpatial.Should().Be(0);
    }

    [Fact]
    public void RecoveryAfterSpikeRestoresFastCaps()
    {
        // Peer goes slow then recovers; caps should bounce back on the next good samples.
        // arrange: baseline seeded low, then a spike
        var state = WarmedUp();
        for (var i = 0; i < 6; i++)
            state.RecordLatency(50);
        for (var i = 0; i < 6; i++)
            state.RecordLatency(1000);
        state.MaxSpatialLayer.Should().Be(0);

        // act: latency returns to near-baseline
        for (var i = 0; i < 6; i++)
            state.RecordLatency(60);

        // assert
        state.IsNetworkFast.Should().BeTrue();
        state.MaxSpatialLayer.Should().Be(2, "fast peer back to top");
        state.MaxTemporalLayer.Should().Be(int.MaxValue);
    }
}
