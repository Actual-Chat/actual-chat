using ActualChat.Streaming;
using static ActualChat.UI.Blazor.App.Services.VideoQualityUI;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class PlaybackVerdictClassifierTest
{
    private static readonly PlaybackThresholds T = PlaybackThresholds.Defaults;

    [Fact]
    public void TargetBuffer_NoSkips_ReturnsGood()
        => PlaybackVerdictClassifier.Classify(T.BufferDurationTooLowMs, 0, T).Should().Be(1);

    [Fact]
    public void LowBuffer_ReturnsBad()
        => PlaybackVerdictClassifier
            .Classify(T.BufferDurationTooLowMs - 1, 0, T)
            .Should().Be(-1);

    [Fact]
    public void LowBuffer_DuringStartupGrace_ReturnsNeutral()
        => PlaybackVerdictClassifier
            .Classify(T.BufferDurationTooLowMs - 1, 0, T, T.StartupGraceMs - 1)
            .Should().Be(0);

    [Fact]
    public void TooMuchBuffer_ReturnsNeutral()
        => PlaybackVerdictClassifier
            .Classify(T.BufferDurationTooHighMs + 1, 0, T)
            .Should().Be(0);

    [Fact]
    public void KeyframeSkip_ReturnsBad()
        => PlaybackVerdictClassifier.Classify(T.BufferDurationTooLowMs, T.KeyframeSkipsBadAtOrAbove, T)
            .Should().Be(-1);

    [Fact]
    public void DecoderQueueTooDeep_ReturnsBad()
        => PlaybackVerdictClassifier
            .Classify(
                T.BufferDurationTooLowMs,
                0,
                T,
                decoderQueueDepthEma: T.DecoderQueueDepthBadAbove + 1)
            .Should().Be(-1);

    [Fact]
    public void QualityReductionRequested_ReturnsBad()
        => PlaybackVerdictClassifier
            .Classify(
                T.BufferDurationTooLowMs,
                0,
                T,
                qualityReductionRequested: true)
            .Should().Be(-1);

    [Fact]
    public void TooHighBoundary_NoSkips_ReturnsGood()
        => PlaybackVerdictClassifier.Classify(T.BufferDurationTooHighMs, 0, T).Should().Be(1);
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
        => new(id, [bytesAtBase, (bytesAtBase + bytesAtTop) / 2, bytesAtTop], MaxLayerId: 2);

    [Fact]
    public void PrimaryFitsAtTop_GetsDefaultQuality()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(1_000_000, primaries, []);

        result.Should().ContainKey("p1");
        result["p1"].Should().Be(ReceiveQuality.Default);
    }

    [Fact]
    public void PrimaryFitsAtTop_WithLayerCap_GetsCappedQuality()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(1_000_000, primaries, [], maxLayerId: 1);

        result["p1"].MaxLayerId.Should().Be(1);
        result["p1"].MaxTemporalLayerId.Should().Be(int.MaxValue);
    }

    [Fact]
    public void PrimaryFitsOnlyAtBase_GetsBaseQuality()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(150_000, primaries, []);

        result["p1"].MaxLayerId.Should().Be(0);
    }

    [Fact]
    public void PrimaryFitsAtTop_WithBaseOnlyCap_GetsBaseQuality()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(1_000_000, primaries, [], maxLayerId: 0);

        result["p1"].MaxLayerId.Should().Be(0);
    }

    [Fact]
    public void PrimaryFitsAtTop_WithPerStreamBaseCap_GetsBaseQuality()
    {
        var primaries = new[] { new StreamRequest("p1", [100_000, 300_000, 500_000], MaxLayerId: 0) };
        var result = Allocator.Allocate(1_000_000, primaries, []);

        result["p1"].MaxLayerId.Should().Be(0);
    }

    [Fact]
    public void PrimaryDoesntFit_OmittedFromResult()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var result = Allocator.Allocate(50_000, primaries, []);

        result.Should().BeEmpty();
    }

    [Fact]
    public void PrimariesGetDesiredFirst_BeforeSecondaries()
    {
        // Budget 1.2 MB/s. p1 wants 500 KB at top, s1 wants 600 KB at top.
        // p1 takes 500 KB → 700 KB remaining. s1 can still get its desired top.
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var secondaries = new[] { Req("s1", 200_000, 600_000) };
        var result = Allocator.Allocate(1_200_000, primaries, secondaries);

        result["p1"].Should().Be(ReceiveQuality.Default);
        result["s1"].MaxLayerId.Should().Be(2);
    }

    [Fact]
    public void SecondaryDegradesToNearestFittingLayerWhenBudgetTight()
    {
        var primaries = new[] { Req("p1", 100_000, 500_000) };
        var secondaries = new[] { new StreamRequest("s1", [100_000, 300_000, 800_000], MaxLayerId: 2) };
        // Budget 800K — fits p1 at top (500K), 300K left, so s1 gets L1.
        var result = Allocator.Allocate(800_000, primaries, secondaries);

        result.Should().ContainKey("p1");
        result["s1"].MaxLayerId.Should().Be(1);
    }
}

public class VideoSizeTest
{
    [Theory]
    [InlineData(VideoSize.W1920, 1080)]
    [InlineData(VideoSize.W1280, 720)]
    [InlineData(VideoSize.W960, 540)]
    [InlineData(VideoSize.W640, 360)]
    [InlineData(VideoSize.W320, 180)]
    public void Height16X9_ReturnsExpectedHeight(VideoSize size, int expectedHeight)
        => size.ShortSide().Should().Be(expectedHeight);

    [Theory]
    [InlineData(0, 0, VideoSize.None)]
    [InlineData(500, 1, VideoSize.W640)]
    [InlineData(500, 2, VideoSize.W1280)]
    [InlineData(480, 3, VideoSize.W1280)]
    [InlineData(960, 2, VideoSize.W1920)]
    public void PickForRenderSize_UsesCssAndCappedDpr(
        double cssLongSide,
        double devicePixelRatio,
        VideoSize expected)
        => VideoSizeExt.FromLongSide(cssLongSide, devicePixelRatio).Should().Be(expected);

    [Fact]
    public void VideoLayerDefs_ExposeH264BaseBitratesKbpsAndCodecEfficiencies()
    {
        VideoLayerDef.CameraLayers.Should().Equal(
            new VideoLayerDef(VideoSourceKind.Camera, VideoSize.W320, 312.5),
            new VideoLayerDef(VideoSourceKind.Camera, VideoSize.W640, 1_250),
            new VideoLayerDef(VideoSourceKind.Camera, VideoSize.W1280, 4_000));
        VideoLayerDef.ScreenCastLayers.Should().Equal(
            new VideoLayerDef(VideoSourceKind.ScreenCast, VideoSize.W960, 4_375),
            new VideoLayerDef(VideoSourceKind.ScreenCast, VideoSize.W1920, 11_375));

        var videoConstants = new AppConstants.VideoConstants();
        videoConstants.CameraLayerBaseBitratesKbps.Should().Equal(312.5, 1_250d, 4_000d);
        videoConstants.ScreenCastLayerBaseBitratesKbps.Should().Equal(4_375d, 11_375d);
        videoConstants.CodecDefs.Should().Contain(new VideoCodecDef(VideoCodecKind.H264, 1));
        videoConstants.CodecDefs.Should().Contain(new VideoCodecDef(VideoCodecKind.Hevc, 2));
    }

    [Fact]
    public void VideoLayerDef_GetBitrateKbps_UsesCodecEfficiency()
    {
        var topCameraLayer = VideoLayerDef.CameraLayers[^1];

        topCameraLayer.GetBitrateKbps(VideoCodecKind.H264).Should().Be(4_000);
        topCameraLayer.GetBitrateKbps(VideoCodecKind.Hevc).Should().Be(2_000);
        topCameraLayer.GetByteRate(VideoCodecKind.Hevc).Should().Be(250_000);
    }

    [Theory]
    [InlineData("avc1.640028", VideoCodecKind.H264)]
    [InlineData("h264", VideoCodecKind.H264)]
    [InlineData("hev1.1.6.L120.B0", VideoCodecKind.Hevc)]
    [InlineData("hvc1.1.6.L120.B0", VideoCodecKind.Hevc)]
    [InlineData("hevc", VideoCodecKind.Hevc)]
    [InlineData("vp09.00.41.08", VideoCodecKind.Vp9)]
    [InlineData("vp9", VideoCodecKind.Vp9)]
    [InlineData("av01.0.08M.08", VideoCodecKind.Av1)]
    [InlineData("bogus", VideoCodecKind.Unknown)]
    public void VideoCodecKind_Parse_ReturnsExpectedKind(string codec, VideoCodecKind expected)
    {
        VideoCodecKindExt.Parse(codec).Should().Be(expected);
    }
}
