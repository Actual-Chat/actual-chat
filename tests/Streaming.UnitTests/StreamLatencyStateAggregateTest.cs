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
        // 3 peers all reporting low stable latency — each gets MaxSpatialLayer=2.
        // Aggregate (max across peers) = 2. Temporal uncapped.
        // arrange
        var state = NewStreamLatencyState();
        RecordWarmed(state, "peer-a", 50);
        RecordWarmed(state, "peer-b", 60);
        RecordWarmed(state, "peer-c", 55);

        // act
        state.EvaluateQuality();

        // assert
        state.QualityPreset.Value.MaxSpatialLayer.Should().Be(2);
        state.QualityPreset.Value.MaxTemporalLayer.Should().Be(int.MaxValue);
    }

    [Fact]
    public void MixedFastAndSlow_AggregateIsMax()
    {
        // One slow peer (cap=0) + one fast peer (cap=2) → aggregate = 2.
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
        state.QualityPreset.Value.MaxSpatialLayer.Should().Be(2, "fast peer still needs top layer");
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

    // --- Bug X: ViewerCount propagation ---
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
        VideoQualityLevel? renderLevel = null)
    {
        var peer = state.GetOrAddWarmedPeerForTests(peerId);
        peer.RecordLatency(latencyMs,
            renderQualityLevel: renderLevel.HasValue ? (int)renderLevel.Value : -1);
    }

    private sealed class TestLoggerProvider(ILogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }
}
