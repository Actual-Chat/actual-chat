using ActualChat.Streaming;
using static ActualChat.UI.Blazor.App.Services.VideoQualityUI;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class RecordingClassifierTest
{
    private static readonly RecordingThresholds T = RecordingThresholds.Defaults;

    private static RecorderStats Snapshot(
        double encodeRatio = 0,
        double senderFrameDropRatio = 0,
        double lastAckMs = 0,
        bool isConnected = true)
        => RecorderStats.Empty with {
            EncodeRatioEma = encodeRatio,
            SenderFrameDropRatioEma = senderFrameDropRatio,
            LastAckAgeMs = lastAckMs,
            IsConnected = isConnected,
        };

    [Fact]
    public void AllGoodSignal_ReturnsPlusOne()
    {
        var h = Snapshot(encodeRatio: 0.2, senderFrameDropRatio: 0, lastAckMs: 100);
        RecordingClassifier.Classify(h, T).Should().Be(1);
    }

    [Fact]
    public void HighEncodeRatio_ReturnsBad()
    {
        var h = Snapshot(encodeRatio: 1.5);
        RecordingClassifier.Classify(h, T).Should().Be(-1);
    }

    [Fact]
    public void HighSenderFrameDropRatio_ReturnsBad()
    {
        var h = Snapshot(senderFrameDropRatio: T.SenderFrameDropRatioBadAbove + 0.01);
        RecordingClassifier.Classify(h, T).Should().Be(-1);
    }

    [Fact]
    public void SenderFrameDropRatioAtThreshold_ReturnsBad()
    {
        var h = Snapshot(senderFrameDropRatio: T.SenderFrameDropRatioBadAbove);
        RecordingClassifier.Classify(h, T).Should().Be(-1);
    }

    [Fact]
    public void SenderFrameDropRatioBelowGoodThreshold_ReturnsGood()
    {
        var h = Snapshot(
            encodeRatio: 0.2,
            senderFrameDropRatio: T.SenderFrameDropRatioGoodBelow - 0.01,
            lastAckMs: 100);
        RecordingClassifier.Classify(h, T).Should().Be(1);
    }

    [Fact]
    public void SenderFrameDropRatioAtGoodThreshold_ReturnsNeutral()
    {
        var h = Snapshot(
            encodeRatio: 0.2,
            senderFrameDropRatio: T.SenderFrameDropRatioGoodBelow,
            lastAckMs: 100);
        RecordingClassifier.Classify(h, T).Should().Be(0);
    }

    [Fact]
    public void HighLastAckAge_ReturnsBad()
    {
        var h = Snapshot(lastAckMs: T.LastAckBadMs + 1);
        RecordingClassifier.Classify(h, T).Should().Be(-1);
    }

    [Fact]
    public void Disconnected_ReturnsNeutral()
    {
        // Even with otherwise-bad signals, disconnected = no decision (neutral).
        var h = Snapshot(encodeRatio: 1.1, senderFrameDropRatio: 1, isConnected: false);
        RecordingClassifier.Classify(h, T).Should().Be(0);
    }

    [Fact]
    public void BetweenThresholds_ReturnsNeutral()
    {
        // encode ratio 0.6 is between GoodBelow (0.33) and BadAbove (1.0)
        var h = Snapshot(encodeRatio: 0.6);
        RecordingClassifier.Classify(h, T).Should().Be(0);
    }

    [Fact]
    public void LastAckUnknown_StillCanReturnGood()
    {
        // lastAckMs == -1 is the sentinel for "never seen an ACK yet".
        var h = Snapshot(encodeRatio: 0.2, lastAckMs: -1, senderFrameDropRatio: 0);
        RecordingClassifier.Classify(h, T).Should().Be(1);
    }
}
