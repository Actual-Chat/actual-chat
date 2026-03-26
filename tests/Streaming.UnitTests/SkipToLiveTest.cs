using ActualChat.Video;
using static ActualChat.Streaming.VideoStreamingBackend;

namespace ActualChat.Streaming.UnitTests;

public class SkipToLiveTest(ILogger log)
{
    private ILogger Log { get; } = log;

    [Fact]
    public void PeerLatencyState_SkipToLive_DefaultsFalse()
    {
        var peer = new PeerLatencyState();
        peer.SkipToLive.Should().BeFalse();
        peer.IsWarmedUp.Should().BeFalse();
        peer.MedianLatencyMs.Should().Be(0);
    }

    [Fact]
    public void PeerLatencyState_RecordLatency_DiscardsDuringWarmup()
    {
        var peer = new PeerLatencyState();
        peer.RecordLatency(9999f);
        peer.MedianLatencyMs.Should().Be(0, "samples during warmup should be discarded");
    }

    [Fact]
    public void StreamLatencyState_ShouldSkipToLive_FalseForUnknownPeer()
    {
        var format = new VideoFormat { Width = 1280, Height = 720 };
        var state = new StreamLatencyState(CpuClock.Instance.Now, format, StateFactory.Default, Log);
        state.ShouldSkipToLive("unknown").Should().BeFalse();
    }

    [Fact]
    public void StreamLatencyState_RecordPeerLatency_NoTriggerDuringWarmup()
    {
        var format = new VideoFormat { Width = 1280, Height = 720 };
        var state = new StreamLatencyState(CpuClock.Instance.Now, format, StateFactory.Default, Log);

        state.RecordPeerLatency("peer1", 5000f);
        state.ShouldSkipToLive("peer1").Should().BeFalse(
            "peer is not warmed up yet, so SkipToLive should not trigger");
    }

    [Fact(Timeout = 20_000)]
    public async Task StreamLatencyState_SkipToLive_FullLifecycle()
    {
        var format = new VideoFormat { Width = 1280, Height = 720 };
        var state = new StreamLatencyState(CpuClock.Instance.Now, format, StateFactory.Default, Log);

        // Register peer with low latency during warmup
        state.RecordPeerLatency("peer1", 100f);

        // Wait past 10s warmup
        await Task.Delay(TimeSpan.FromSeconds(11));

        // Below threshold — should not trigger
        state.RecordPeerLatency("peer1", 2000f);
        state.ShouldSkipToLive("peer1").Should().BeFalse(
            "latency 2000ms is below the 3000ms threshold");

        // Above threshold — should trigger
        state.RecordPeerLatency("peer1", 4000f);
        state.ShouldSkipToLive("peer1").Should().BeTrue(
            "latency 4000ms exceeds the 3000ms threshold after warmup");

        // Clear and verify
        state.ClearSkipToLive("peer1");
        state.ShouldSkipToLive("peer1").Should().BeFalse(
            "SkipToLive should be false after clearing");

        // Re-trigger works
        state.RecordPeerLatency("peer1", 3500f);
        state.ShouldSkipToLive("peer1").Should().BeTrue(
            "SkipToLive should re-trigger after clearing");
    }

    [Fact(Timeout = 20_000)]
    public async Task StreamLatencyState_SkipToLive_MultiPeerIndependence()
    {
        var format = new VideoFormat { Width = 1280, Height = 720 };
        var state = new StreamLatencyState(CpuClock.Instance.Now, format, StateFactory.Default, Log);

        // Register both peers during warmup
        state.RecordPeerLatency("peer1", 100f);
        state.RecordPeerLatency("peer2", 100f);

        // Wait past warmup
        await Task.Delay(TimeSpan.FromSeconds(11));

        // Trigger skip for peer1 only
        state.RecordPeerLatency("peer1", 4000f);
        state.RecordPeerLatency("peer2", 100f);

        state.ShouldSkipToLive("peer1").Should().BeTrue(
            "peer1 reported high latency and should skip");
        state.ShouldSkipToLive("peer2").Should().BeFalse(
            "peer2 has low latency and should not skip");
    }
}
