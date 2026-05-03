using ActualChat.Streaming;
using static ActualChat.UI.Blazor.App.Services.VideoQualityUI;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class PlaybackVerdictClassifierTest
{
    private static readonly PlaybackThresholds T = PlaybackThresholds.Defaults;

    [Fact]
    public void TargetBuffer_NoSkips_ReturnsGood()
        => PlaybackVerdictClassifier.Classify(T.BufferDurationMsBadBelow, 0, T).Should().Be(1);

    [Fact]
    public void LowBuffer_AfterStartupGrace_ReturnsBad()
        => PlaybackVerdictClassifier
            .Classify(T.BufferDurationMsBadBelow - 1, 0, T, T.StartupGraceMs)
            .Should().Be(-1);

    [Fact]
    public void LowBuffer_DuringStartupGrace_ReturnsNeutral()
        => PlaybackVerdictClassifier
            .Classify(T.BufferDurationMsBadBelow - 1, 0, T, T.StartupGraceMs - 1)
            .Should().Be(0);

    [Fact]
    public void TooMuchBuffer_ReturnsNeutral()
        => PlaybackVerdictClassifier
            .Classify(T.BufferDurationMsTooHighAbove + 1, 0, T)
            .Should().Be(0);

    [Fact]
    public void KeyframeSkip_ReturnsBad()
        => PlaybackVerdictClassifier.Classify(T.BufferDurationMsBadBelow, T.KeyframeSkipsBadAtOrAbove, T)
            .Should().Be(-1);

    [Fact]
    public void DecoderQueueTooDeep_ReturnsBad()
        => PlaybackVerdictClassifier
            .Classify(
                T.BufferDurationMsBadBelow,
                0,
                T,
                decoderQueueDepthP90: T.DecoderQueueDepthBadAbove + 1)
            .Should().Be(-1);

    [Fact]
    public void QualityReductionRequested_ReturnsBad()
        => PlaybackVerdictClassifier
            .Classify(
                T.BufferDurationMsBadBelow,
                0,
                T,
                qualityReductionRequested: true)
            .Should().Be(-1);

    [Fact]
    public void TooHighBoundary_NoSkips_ReturnsGood()
        => PlaybackVerdictClassifier.Classify(T.BufferDurationMsTooHighAbove, 0, T).Should().Be(1);
}

public class AggregateHealthTest
{
    [Fact]
    public void Empty_ReturnsZero()
        => AggregateHealth.Compute(Array.Empty<(long, int)>()).Should().Be(0);

    [Fact]
    public void BigHealthy_PlusSmallLagging_TrendsTowardZero()
    {
        // Big healthy (+1) at 1 MB/s vs small lagging (-1) at 50 KB/s.
        // Weighted by rate: most bandwidth is healthy → aggregate ≈ +0.9.
        var signals = new (long, int)[] { (1_000_000, 1), (50_000, -1) };
        var result = AggregateHealth.Compute(signals);
        result.Should().BeGreaterThan(0.85).And.BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void SmallHealthy_PlusBigLagging_TrendsNegative()
    {
        // Small healthy (+1) at 50 KB/s vs big lagging (-1) at 1 MB/s.
        var signals = new (long, int)[] { (50_000, 1), (1_000_000, -1) };
        var result = AggregateHealth.Compute(signals);
        result.Should().BeLessThan(-0.85);
    }

    [Fact]
    public void EqualWeights_AverageVerdicts()
    {
        var signals = new (long, int)[] { (100_000, -1), (100_000, 1) };
        AggregateHealth.Compute(signals).Should().BeApproximately(0, 1e-9);
    }
}

public class CapacityEstimatorTest
{
    private static readonly PlaybackThresholds T = PlaybackThresholds.Defaults;

    [Fact]
    public void StartsAtColdStart()
    {
        new CapacityEstimator(T).Capacity.Should().Be(T.ColdStartCapacityBytesPerSec);
    }

    [Fact]
    public void GoodAggregate_ClimbsTowardsSqrt2OfRate()
    {
        // Reset to a low capacity, then climb on +1 aggregate at high rate.
        var est = new CapacityEstimator(T);
        // Force backoff to take it below the climb ceiling.
        est.Step(-1, 0); // backoff
        est.Step(-1, 0); // backoff again
        var lowCap = est.Capacity;

        // Climb with rate = 1 MB/s, expect new ceiling = √2 × 1 MB/s ≈ 1.414 MB/s,
        // unless that's lower than the current capacity (then hold).
        var newCap = est.Step(1, 1_000_000);
        var expectedCeiling = (long)(1_000_000 * T.ClimbCap);
        if (expectedCeiling > lowCap)
            newCap.Should().Be(expectedCeiling);
        else
            newCap.Should().Be(lowCap);
    }

    [Fact]
    public void BadAggregate_BacksOffByFactor()
    {
        var est = new CapacityEstimator(T);
        var before = est.Capacity;
        var after = est.Step(-1, 0);
        after.Should().BeLessThan(before);
        // Allow rounding slack
        ((double)after).Should().BeApproximately(before * T.BackoffFactor, 1.0);
    }

    [Fact]
    public void HoldBand_NeutralAggregate_Unchanged()
    {
        var est = new CapacityEstimator(T);
        var before = est.Capacity;
        var after = est.Step(0, 1_000_000);
        after.Should().Be(before);
    }

    [Fact]
    public void Floor_DoesNotGoBelowMinCapacity()
    {
        var est = new CapacityEstimator(T);
        for (var i = 0; i < 50; i++)
            est.Step(-1, 0);
        est.Capacity.Should().BeGreaterThanOrEqualTo(T.MinCapacityBytesPerSec);
    }
}

public class AllocatorTest
{
    private static StreamRequest Req(string id, long bytesAtBase, long bytesAtTop)
        => new(id, bytesAtBase, bytesAtTop);

    [Fact]
    public void PrimaryFitsAtTop_GetsDefaultQuality()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(1_000_000, primaries, []);

        result.Should().ContainKey("p1");
        result["p1"].Should().Be(ReceiveQuality.Default);
    }

    [Fact]
    public void PrimaryFitsAtTop_WithSpatialCap_GetsCappedQuality()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(1_000_000, primaries, [], maxSpatialLayer: 1);

        result["p1"].MaxSpatialLayer.Should().Be(1);
        result["p1"].MaxTemporalLayer.Should().Be(int.MaxValue);
    }

    [Fact]
    public void PrimaryFitsOnlyAtBase_GetsBaseQuality()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(150_000, primaries, []);

        result["p1"].MaxSpatialLayer.Should().Be(0);
    }

    [Fact]
    public void PrimaryFitsAtTop_WithBaseOnlyCap_GetsBaseQuality()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(1_000_000, primaries, [], maxSpatialLayer: 0);

        result["p1"].MaxSpatialLayer.Should().Be(0);
    }

    [Fact]
    public void PrimaryFitsAtTop_WithPerStreamBaseCap_GetsBaseQuality()
    {
        var primaries = new[] { new StreamRequest("p1", 100_000, 500_000, MaxSpatialLayer: 0) };
        var result = Allocator.Allocate(1_000_000, primaries, []);

        result["p1"].MaxSpatialLayer.Should().Be(0);
    }

    [Fact]
    public void PrimaryDoesntFit_OmittedFromResult()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(50_000, primaries, []);

        result.Should().BeEmpty();
    }

    [Fact]
    public void PrimariesGetTopFirst_BeforeSecondariesGetBase()
    {
        // Budget 1.2 MB/s. p1 wants 500 KB at top, s1 wants 200 KB at base.
        // p1 takes 500 KB → 700 KB remaining. s1 takes 200 KB → 500 KB remaining.
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var secondaries = new[] { Req("s1", 200_000, 600_000) };
        var result = Allocator.Allocate(1_200_000, primaries, secondaries);

        result["p1"].Should().Be(ReceiveQuality.Default);
        result["s1"].MaxSpatialLayer.Should().Be(0);
    }

    [Fact]
    public void SecondaryDroppedWhenBudgetTight()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var secondaries = new[] { Req("s1", 500_000, 800_000) };
        // Budget 600K — fits p1 at top (500K), 100K left, can't fit s1 base (500K)
        var result = Allocator.Allocate(600_000, primaries, secondaries);

        result.Should().ContainKey("p1");
        result.Should().NotContainKey("s1");
    }
}
