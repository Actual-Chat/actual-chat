using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class SenderHealthClassifierTest
{
    private static SenderHealthThresholds T() => SenderHealthThresholds.Defaults with {
        BadStreakRequired = 2,
        GoodStreakRequired = 3,
    };

    [Fact]
    public void HighEncodeRatio_LowAckAge_EncoderBad_UplinkGood()
    {
        var c = new SenderHealthClassifier(T());
        EncoderHealth enc = EncoderHealth.Empty;
        UplinkHealth up = UplinkHealth.Empty;
        for (var i = 0; i < 3; i++) {
            enc = c.ClassifyEncoder(
                encodeRatioEma: 2.5,
                encodeQueueDepthEma: 0,
                restartStreakIn60s: 0,
                senderEncodePathDropRatio: 0,
                isTabBackgrounded: false);
            up = c.ClassifyUplink(
                wireLastAckAgeMs: 100,
                wireQueueDepthEma: 0,
                floodGateSkipPerSec: 0,
                peerReconnectStreak: 0,
                senderWirePathDropRatio: 0);
        }
        enc.Verdict.Should().Be(HealthVerdict.Bad);
        up.Verdict.Should().Be(HealthVerdict.Good);
    }

    [Fact]
    public void LowEncodeRatio_HighAckAge_EncoderGood_UplinkBad()
    {
        var c = new SenderHealthClassifier(T());
        EncoderHealth enc = EncoderHealth.Empty;
        UplinkHealth up = UplinkHealth.Empty;
        for (var i = 0; i < 3; i++) {
            enc = c.ClassifyEncoder(
                encodeRatioEma: 0.5,
                encodeQueueDepthEma: 0,
                restartStreakIn60s: 0,
                senderEncodePathDropRatio: 0,
                isTabBackgrounded: false);
            up = c.ClassifyUplink(
                wireLastAckAgeMs: 3_000,
                wireQueueDepthEma: 0,
                floodGateSkipPerSec: 0,
                peerReconnectStreak: 0,
                senderWirePathDropRatio: 0);
        }
        enc.Verdict.Should().Be(HealthVerdict.Good);
        up.Verdict.Should().Be(HealthVerdict.Bad);
    }

    [Fact]
    public void TabBackgrounded_RelaxesEncodeRatioThreshold()
    {
        var c = new SenderHealthClassifier(T());
        // Ratio 2.5 — Bad under fg threshold (2.0), Good under bg threshold (3.0).
        EncoderHealth enc = EncoderHealth.Empty;
        for (var i = 0; i < 4; i++) {
            enc = c.ClassifyEncoder(
                encodeRatioEma: 2.5,
                encodeQueueDepthEma: 0,
                restartStreakIn60s: 0,
                senderEncodePathDropRatio: 0,
                isTabBackgrounded: true);
        }
        enc.Verdict.Should().NotBe(HealthVerdict.Bad);
    }

    [Fact]
    public void EncodeRatioBad_RequiresStreak()
    {
        var c = new SenderHealthClassifier(T());
        // One bad tick is not enough; BadStreakRequired = 2.
        var enc = c.ClassifyEncoder(
            encodeRatioEma: 2.5,
            encodeQueueDepthEma: 0,
            restartStreakIn60s: 0,
            senderEncodePathDropRatio: 0,
            isTabBackgrounded: false);
        enc.Verdict.Should().NotBe(HealthVerdict.Bad);
        // Second consecutive bad tick promotes to Bad.
        enc = c.ClassifyEncoder(
            encodeRatioEma: 2.5,
            encodeQueueDepthEma: 0,
            restartStreakIn60s: 0,
            senderEncodePathDropRatio: 0,
            isTabBackgrounded: false);
        enc.Verdict.Should().Be(HealthVerdict.Bad);
    }

    [Fact]
    public void RestartStreak_AtThreshold_IsBad()
    {
        var c = new SenderHealthClassifier(T());
        // RestartStreak is itself a streaked count — no extra hysteresis.
        var enc = c.ClassifyEncoder(
            encodeRatioEma: 0.5,
            encodeQueueDepthEma: 0,
            restartStreakIn60s: SenderHealthThresholds.Defaults.RestartStreakBad,
            senderEncodePathDropRatio: 0,
            isTabBackgrounded: false);
        enc.Verdict.Should().Be(HealthVerdict.Bad);
    }

    [Fact]
    public void PeerReconnect_OneTick_PromotesUplinkBad()
    {
        var c = new SenderHealthClassifier(T());
        var up = c.ClassifyUplink(
            wireLastAckAgeMs: 100,
            wireQueueDepthEma: 0,
            floodGateSkipPerSec: 0,
            peerReconnectStreak: 1,
            senderWirePathDropRatio: 0);
        up.Verdict.Should().Be(HealthVerdict.Bad);
    }
}
