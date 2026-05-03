using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using static ActualChat.UI.Blazor.App.Services.VideoQualityUI;

namespace ActualChat.Chat.UI.Blazor.UnitTests.VideoQualityUITests;

public class RecordingClassifierTest
{
    private static readonly RecordingThresholds T = RecordingThresholds.Defaults;

    private static RecorderHealthSnapshot Snapshot(
        double encodeAvg = 0,
        double? encodeP90 = null,
        double slotRate = 0,
        double backlogMs = 0,
        int skips = 0,
        double lastAckMs = 0,
        bool isConnected = true)
        => new(encodeAvg, encodeP90 ?? encodeAvg, slotRate, backlogMs, skips, lastAckMs, isConnected);

    [Fact]
    public void AllGoodSignal_ReturnsPlusOne()
    {
        // arrange
        var h = Snapshot(encodeAvg: 0.2, backlogMs: 10, skips: 0, lastAckMs: 100);

        // act
        var result = RecordingClassifier.Classify(h, T);

        // assert
        result.Should().Be(1);
    }

    [Fact]
    public void HighEncodeRatio_ReturnsBad()
    {
        var h = Snapshot(encodeAvg: 1.1);
        RecordingClassifier.Classify(h, T).Should().Be(-1);
    }

    [Fact]
    public void HighP90WithGoodAverage_ReturnsGood()
    {
        var h = Snapshot(encodeAvg: 0.2, encodeP90: 1.1, backlogMs: 10, skips: 0, lastAckMs: 100);
        RecordingClassifier.Classify(h, T).Should().Be(1);
    }

    [Fact]
    public void HighBacklog_ReturnsBad()
    {
        var h = Snapshot(backlogMs: T.BacklogBadMs + 1);
        RecordingClassifier.Classify(h, T).Should().Be(-1);
    }

    [Fact]
    public void HighLastAckAge_ReturnsBad()
    {
        var h = Snapshot(lastAckMs: T.LastAckBadMs + 1);
        RecordingClassifier.Classify(h, T).Should().Be(-1);
    }

    [Fact]
    public void ManySkips_ReturnsBad()
    {
        var h = Snapshot(skips: (int)T.SkipsBadCount);
        RecordingClassifier.Classify(h, T).Should().Be(-1);
    }

    [Fact]
    public void Disconnected_ReturnsNeutral()
    {
        // Even with otherwise-bad signals, disconnected = no decision (neutral).
        var h = Snapshot(encodeAvg: 1.1, backlogMs: 5000, isConnected: false);
        RecordingClassifier.Classify(h, T).Should().Be(0);
    }

    [Fact]
    public void BetweenThresholds_ReturnsNeutral()
    {
        // encode ratio 0.6 is between GoodBelow (0.33) and BadAbove (1.0)
        var h = Snapshot(encodeAvg: 0.6);
        RecordingClassifier.Classify(h, T).Should().Be(0);
    }

    [Fact]
    public void LastAckUnknown_StillCanReturnGood()
    {
        // lastAckMs == -1 is the sentinel for "never seen an ACK yet".
        var h = Snapshot(encodeAvg: 0.2, lastAckMs: -1, backlogMs: 10);
        RecordingClassifier.Classify(h, T).Should().Be(1);
    }
}
