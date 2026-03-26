using ActualChat.Video;
using static ActualChat.Streaming.VideoStreamingBackend;

namespace ActualChat.Streaming.UnitTests;

public class SkipToLiveTest(ILogger log)
{
    private ILogger Log { get; } = log;

    [Fact]
    public void PeerLatencyState_Defaults()
    {
        var peer = new PeerLatencyState();
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

    [Fact(Timeout = 20_000)]
    public async Task PeerLatencyState_RecordLatency_AcceptsAfterWarmup()
    {
        var peer = new PeerLatencyState();
        peer.RecordLatency(9999f);
        peer.MedianLatencyMs.Should().Be(0);

        // Wait past 10s warmup
        await Task.Delay(TimeSpan.FromSeconds(11));

        peer.RecordLatency(500f);
        peer.MedianLatencyMs.Should().Be(500f, "samples should be accepted after warmup");
    }

    [Fact]
    public void PeerLatencyState_IsReceiverBound()
    {
        var peer = new PeerLatencyState();
        peer.IsReceiverBound.Should().BeFalse("default values should not indicate receiver-bound");
    }
}
