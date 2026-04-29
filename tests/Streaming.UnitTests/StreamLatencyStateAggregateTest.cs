using ActualChat.Video;
using ActualLab.Fusion;
using static ActualChat.Streaming.StreamLatencyStore;

namespace ActualChat.Streaming.UnitTests;

public class StreamLatencyStateAggregateTest(ILogger log)
{
    private ILogger Log { get; } = log;

    [Fact]
    public void NoPeers_QualityPresetHasDefaultLayerCaps()
    {
        // arrange
        var state = NewStreamLatencyState();

        // act: no peers, no RecordPeerLatency
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.MaxSpatialLayer.Should().Be(int.MaxValue, "no peers → no cap");
        state.QualityPreset.Value.MaxTemporalLayer.Should().Be(int.MaxValue);
    }

    [Fact]
    public void AllPeersFast_AggregateIsTopLayer()
    {
        // 3 peers all reporting low stable latency — each gets MaxSpatialLayer=int.MaxValue
        // (uncapped). Aggregate (max across peers) = int.MaxValue. Temporal uncapped.
        // The sender's filter resolves the actual layer at delivery time using
        // observedMaxSpatial; the aggregate just signals "no peer is asking for less".
        // arrange
        var state = NewStreamLatencyState();
        RecordWarmed(state, "peer-a", 50);
        RecordWarmed(state, "peer-b", 60);
        RecordWarmed(state, "peer-c", 55);

        // act
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.MaxSpatialLayer.Should().Be(int.MaxValue);
        state.QualityPreset.Value.MaxTemporalLayer.Should().Be(int.MaxValue);
    }

    [Fact]
    public void MixedFastAndSlow_AggregateIsMax()
    {
        // One slow peer (cap=0) + one fast peer (cap=int.MaxValue) → aggregate = int.MaxValue.
        // Sender still produces all layers; filter caps the slow peer per-peer.
        // arrange
        var state = NewStreamLatencyState();
        RecordWarmed(state, "peer-fast", 50);
        // Seed baseline for slow peer, then spike
        for (var i = 0; i < 6; i++)
            RecordWarmed(state, "peer-slow", 50);
        for (var i = 0; i < 6; i++)
            RecordWarmed(state, "peer-slow", 1500);

        // act
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.MaxSpatialLayer.Should().Be(int.MaxValue, "fast peer still needs top layer");
    }

    [Fact]
    public void AllPeersSlow_AggregateIsBaseLayer()
    {
        // arrange
        var state = NewStreamLatencyState();
        for (var i = 0; i < 6; i++) {
            RecordWarmed(state, "peer-a", 50);
            RecordWarmed(state, "peer-b", 50);
        }
        for (var i = 0; i < 6; i++) {
            RecordWarmed(state, "peer-a", 1500);
            RecordWarmed(state, "peer-b", 1500);
        }

        // act
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.MaxSpatialLayer.Should().Be(0, "all peers slow → no enhancement needed");
        state.QualityPreset.Value.MaxTemporalLayer.Should().Be(0);
    }

    [Fact]
    public void RenderHintFromAllPeers_AggregateFollowsMaxHint()
    {
        // peer-a pins focus (High render hint), peer-b is sidebar (Low). Aggregate = 2.
        // arrange
        var state = NewStreamLatencyState();
        for (var i = 0; i < 6; i++) {
            RecordWarmed(state, "peer-focus", 50, VideoQualityLevel.High);
            RecordWarmed(state, "peer-sidebar", 50, VideoQualityLevel.Low);
        }

        // act
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.MaxSpatialLayer.Should().Be(2, "focused peer needs top layer");
    }

    [Fact]
    public void AllPeersSidebar_AggregateIsMidLayer()
    {
        // All receivers render small sidebar tiles → no one needs the top simulcast
        // layer. Sender can spin down its 720p encoder, keep 360p mid tier running.
        // Base tier alone would be too low for a 640x360 sidebar canvas (upscale).
        // arrange
        var state = NewStreamLatencyState();
        for (var i = 0; i < 6; i++) {
            RecordWarmed(state, "peer-a", 50, VideoQualityLevel.Low);
            RecordWarmed(state, "peer-b", 50, VideoQualityLevel.Low);
        }

        // act
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.MaxSpatialLayer.Should().Be(1, "all sidebar → mid tier (~360p), not base upscale");
    }

    // --- Receiver-bound step-down ---
    // EvaluateQuality previously gated all classification behind IsNetworkSlow,
    // so peers with a struggling decoder but healthy network never tripped a
    // step-down — the sender kept producing the same resolution that the
    // receiver couldn't keep up with. The fix decouples receiver-bound
    // classification from IsNetworkSlow and runs its own step-down clause
    // ahead of the network-slow path.

    [Fact]
    public void AllPeersReceiverBound_StepsDownPreset()
    {
        // 3 peers, all reporting healthy network latency but slow decoder
        // (medianDecodeTime > HighDecodeTimeThresholdMs). The receiver-bound
        // ratio = 1.0 > PeerOutlierRatio. After LatencyStepDownConsecutiveChecks
        // (2) consecutive evaluations the preset must step down.
        // arrange
        var state = NewStreamLatencyState();
        // Webcam streams start at High; StepDown(High) = Medium.
        state.QualityPreset.Value.Level.Should().Be(VideoQualityLevel.High, "Webcam default");

        // act: feed 2 evaluations with all peers receiver-bound
        for (var tick = 0; tick < 2; tick++) {
            RecordWarmed(state, "peer-a", 50, medianDecodeTimeMs: 150f);
            RecordWarmed(state, "peer-b", 60, medianDecodeTimeMs: 150f);
            RecordWarmed(state, "peer-c", 55, medianDecodeTimeMs: 150f);
            state.EvaluateQuality();
        }

        // assert
        state.QualityPreset.Value.Level.Should().Be(VideoQualityLevel.Medium,
            "all peers receiver-bound for 2 consecutive evaluations → step down High→Medium");
    }

    [Fact]
    public void OneOfTwoReceiverBound_StepsDown()
    {
        // 2-peer call: PeerOutlierRatioSmallCall = 0.34, so a single decoder-bound
        // peer (1/2 = 0.5 > 0.34) trips the step-down on the small-call branch.
        // arrange
        var state = NewStreamLatencyState();

        // act
        for (var tick = 0; tick < 2; tick++) {
            RecordWarmed(state, "peer-fast", 50);
            RecordWarmed(state, "peer-slow-decoder", 50, medianDecodeTimeMs: 150f);
            state.EvaluateQuality();
        }

        // assert
        state.QualityPreset.Value.Level.Should().Be(VideoQualityLevel.Medium,
            "1/2 receiver-bound exceeds PeerOutlierRatioSmallCall → step down High→Medium");
    }

    [Fact]
    public void SingleReceiverBoundEvaluation_DoesNotStepDown()
    {
        // Hysteresis: a single bad evaluation must not collapse quality.
        // LatencyStepDownConsecutiveChecks=2 → first tick is "deferred", quality
        // unchanged. This protects against a transient decoder hiccup (GC pause,
        // codec warmup overshoot) cycling the encoder needlessly.
        // arrange
        var state = NewStreamLatencyState();
        var initialLevel = state.QualityPreset.Value.Level;

        // act
        RecordWarmed(state, "peer-a", 50, medianDecodeTimeMs: 150f);
        RecordWarmed(state, "peer-b", 50, medianDecodeTimeMs: 150f);
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.Level.Should().Be(initialLevel,
            "first receiver-bound evaluation defers — needs 2 consecutive to step down");
    }

    // --- ViewerCount propagation ---
    // VideoQualityPreset.ViewerCount tells the publisher how many peers are
    // currently subscribed to its stream. Without this, an asymmetric publisher
    // (one that pushes but doesn't watch anything) stays in P2P mode forever
    // because its incoming-stream count is 0, and simulcast never arms.

    [Fact]
    public void NoPeers_ViewerCountIsZero()
    {
        // arrange
        var state = NewStreamLatencyState();

        // act
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.ViewerCount.Should().Be(0, "no peers reporting → no viewers");
    }

    [Fact]
    public void OneViewer_ViewerCountIsOne()
    {
        // arrange
        var state = NewStreamLatencyState();
        RecordWarmed(state, "peer-only-watcher", 50);

        // act
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.ViewerCount.Should().Be(1,
            "one peer reporting latency = one active subscriber to this stream");
    }

    [Fact]
    public void ThreeViewers_ViewerCountReflectsPeerCount()
    {
        // arrange
        var state = NewStreamLatencyState();
        RecordWarmed(state, "peer-a", 50);
        RecordWarmed(state, "peer-b", 60);
        RecordWarmed(state, "peer-c", 55);

        // act
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.ViewerCount.Should().Be(3,
            "publisher should know how many peers are subscribed");
    }

    // --- OVER-DELIVERY skipped while simulcast is active ---
    // RecordFrameBytes counts every byte across every spatial layer. The single-layer
    // bitrate target (VideoBitrateTable.GetExpectedBitrate) was tripping a 2.5x
    // OVER-DELIVERY step-down on every simulcast stream — the multi-encoder sum is
    // legitimate, not VBR overshoot. Once spatialLayerId>0 is observed, the check
    // must be suppressed.

    [Fact]
    public void IsSimulcastActive_StartsFalse()
    {
        // arrange
        var state = NewStreamLatencyState();

        // assert
        state.IsSimulcastActive.Should().BeFalse("no frames recorded yet");
    }

    [Fact]
    public void IsSimulcastActive_StaysFalseForSingleEncoder()
    {
        // arrange
        var state = NewStreamLatencyState();

        // act: many frames at spatial=0 only
        for (var i = 0; i < 100; i++)
            state.RecordFrameBytes(50_000, spatialLayerId: 0);

        // assert
        state.IsSimulcastActive.Should().BeFalse("base-layer-only stream is single-encoder");
    }

    [Fact]
    public void IsSimulcastActive_FlipsTrueOnFirstExtraLayer()
    {
        // arrange
        var state = NewStreamLatencyState();
        state.RecordFrameBytes(50_000, spatialLayerId: 0);
        state.IsSimulcastActive.Should().BeFalse();

        // act: first frame on spatial=1
        state.RecordFrameBytes(20_000, spatialLayerId: 1);

        // assert
        state.IsSimulcastActive.Should().BeTrue("second simulcast layer observed");
    }

    [Fact]
    public void IsSimulcastActive_RemainsTrueWhenLowerLayersArrive()
    {
        // Once any extra layer is seen, the flag latches — even if the next frame
        // happens to come on spatial=0. Monotonic. (Filter callers shouldn't see
        // it flipping back and forth across a single keyframe burst.)
        // arrange
        var state = NewStreamLatencyState();
        state.RecordFrameBytes(50_000, spatialLayerId: 2);
        state.IsSimulcastActive.Should().BeTrue();

        // act
        for (var i = 0; i < 10; i++)
            state.RecordFrameBytes(50_000, spatialLayerId: 0);

        // assert
        state.IsSimulcastActive.Should().BeTrue("max-observed is monotonic");
    }

    // Helpers

    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    private StreamLatencyState NewStreamLatencyState()
    {
        var services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(new TestLoggerProvider(Log)).SetMinimumLevel(LogLevel.Debug))
            .AddFusion(_ => { })
            .BuildServiceProvider();
        var stateFactory = services.StateFactory();
        var format = new VideoFormat { Codec = "avc1", Width = 1280, Height = 720 };
        return new StreamLatencyState(
            TestChatId,
            Moment.MinValue,
            format,
            StreamKind.Webcam,
            stateFactory,
            Log);
    }

    // Feeds a latency sample via the peer state's warmup-bypassing path. We can't
    // easily route through StreamLatencyState.RecordPeerLatency because it creates
    // a fresh PeerLatencyState with the default CpuTimestamp.Now as _createdAt,
    // forcing a 10s warmup wait. Instead, access the internal peer dict and inject
    // a pre-warmed PeerLatencyState directly.
    private static void RecordWarmed(StreamLatencyState state, string peerId, float latencyMs,
        VideoQualityLevel? renderLevel = null, float medianDecodeTimeMs = -1, int bufferDepth = -1)
    {
        var peer = state.GetOrAddWarmedPeerForTests(peerId);
        peer.RecordLatency(latencyMs,
            medianDecodeTimeMs: medianDecodeTimeMs,
            bufferDepth: bufferDepth,
            renderQualityLevel: renderLevel.HasValue ? (int)renderLevel.Value : -1);
    }

    private sealed class TestLoggerProvider(ILogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }
}
