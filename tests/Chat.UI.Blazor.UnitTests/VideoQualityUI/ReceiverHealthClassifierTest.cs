using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ReceiverHealthClassifierTest
{
    private static ReceiverHealthThresholds T() => ReceiverHealthThresholds.Defaults with {
        DownlinkLatencyBadStreak = 2,
        DownlinkLatencyGoodStreak = 3,
        ByteRateDeficitBadStreak = 2,
        ByteRateDeficitGoodStreak = 3,
        DecodeRatioBadStreak = 2,
        DecodeRatioGoodStreak = 3,
    };

    [Fact]
    public void HighDownlinkLatency_LowDecodeRatio_DownlinkBad_DecoderGood()
    {
        var c = new ReceiverHealthClassifier(T());
        DownlinkHealth dl = DownlinkHealth.Empty;
        DecoderHealth dec = DecoderHealth.Empty;
        for (var i = 0; i < 3; i++) {
            dl = c.ClassifyDownlink(
                serverToReceiverLatencyEma: 400,
                arrivalIntervalEma: 33,
                serverPathDropRatio: 0,
                bufferUnderrunRatio: 0,
                incomingByteRateDeficit: 1.0);
            dec = c.ClassifyDecoder(
                decodeRatioEma: 0.5,
                hangRateIn60s: 0,
                recoveryStreak: 0,
                presentSkipRatio: 0,
                receiverDecodePathDropRatio: 0);
        }
        dl.Verdict.Should().Be(HealthVerdict.Bad);
        dec.Verdict.Should().Be(HealthVerdict.Good);
    }

    [Fact]
    public void LowDownlinkLatency_HighDecodeRatio_DownlinkGood_DecoderBad()
    {
        var c = new ReceiverHealthClassifier(T());
        DownlinkHealth dl = DownlinkHealth.Empty;
        DecoderHealth dec = DecoderHealth.Empty;
        for (var i = 0; i < 3; i++) {
            dl = c.ClassifyDownlink(
                serverToReceiverLatencyEma: 20,
                arrivalIntervalEma: 33,
                serverPathDropRatio: 0,
                bufferUnderrunRatio: 0,
                incomingByteRateDeficit: 1.0);
            dec = c.ClassifyDecoder(
                decodeRatioEma: 2.0,
                hangRateIn60s: 0,
                recoveryStreak: 0,
                presentSkipRatio: 0,
                receiverDecodePathDropRatio: 0);
        }
        dl.Verdict.Should().Be(HealthVerdict.Good);
        dec.Verdict.Should().Be(HealthVerdict.Bad);
    }

    [Fact]
    public void DecoderHang_OneEvent_PromotesDecoderBad()
    {
        var c = new ReceiverHealthClassifier(T());
        var dec = c.ClassifyDecoder(
            decodeRatioEma: 0.5,
            hangRateIn60s: 1,
            recoveryStreak: 0,
            presentSkipRatio: 0,
            receiverDecodePathDropRatio: 0);
        dec.Verdict.Should().Be(HealthVerdict.Bad);
    }

    [Fact]
    public void RecoveryStreak_AtThreshold_DoesNotDriveBad()
    {
        // recoveryStreak is recorded on DecoderHealth but no longer drives the
        // combined verdict — it fires on decoder restart events, which are not
        // direct decoder fault.
        var c = new ReceiverHealthClassifier(T());
        var dec = c.ClassifyDecoder(
            decodeRatioEma: 0.5,
            hangRateIn60s: 0,
            recoveryStreak: ReceiverHealthThresholds.Defaults.RecoveryStreakBad,
            presentSkipRatio: 0,
            receiverDecodePathDropRatio: 0);
        dec.Verdict.Should().NotBe(HealthVerdict.Bad);
        dec.RecoveryStreak.Should().Be(ReceiverHealthThresholds.Defaults.RecoveryStreakBad);
    }

    [Fact]
    public void BufferUnderrun_RequiresStreak()
    {
        var c = new ReceiverHealthClassifier(T());
        // One tick of underrun above threshold isn't enough (DownlinkLatencyBadStreak = 2 in T()).
        var dl = c.ClassifyDownlink(
            serverToReceiverLatencyEma: 0,
            arrivalIntervalEma: 33,
            serverPathDropRatio: 0,
            bufferUnderrunRatio: 0.5,
            incomingByteRateDeficit: 1.0);
        dl.Verdict.Should().NotBe(HealthVerdict.Bad);
        dl = c.ClassifyDownlink(
            serverToReceiverLatencyEma: 0,
            arrivalIntervalEma: 33,
            serverPathDropRatio: 0,
            bufferUnderrunRatio: 0.5,
            incomingByteRateDeficit: 1.0);
        dl.Verdict.Should().Be(HealthVerdict.Bad);
    }

    [Fact]
    public void DriftUnexplainedByDecode_DefaultsToDownlink()
    {
        // Plan §"Attribution": when DecodeRatioEma is healthy AND
        // ServerToReceiverLatencyEma is bad, the verdict is Downlink, not Decoder.
        var c = new ReceiverHealthClassifier(T());
        DownlinkHealth dl = DownlinkHealth.Empty;
        DecoderHealth dec = DecoderHealth.Empty;
        for (var i = 0; i < 3; i++) {
            dl = c.ClassifyDownlink(
                serverToReceiverLatencyEma: 400,
                arrivalIntervalEma: 33,
                serverPathDropRatio: 0,
                bufferUnderrunRatio: 0,
                incomingByteRateDeficit: 1.0);
            dec = c.ClassifyDecoder(
                decodeRatioEma: 0.5,
                hangRateIn60s: 0,
                recoveryStreak: 0,
                presentSkipRatio: 0,
                receiverDecodePathDropRatio: 0);
        }
        dl.Verdict.Should().Be(HealthVerdict.Bad);
        dec.Verdict.Should().NotBe(HealthVerdict.Bad);
    }

    [Fact]
    public void PresentSkipHigh_NoOtherSignal_DoesNotDriveBad()
    {
        // presentSkipRatio fires on MSTG catch-up skips, not decoder fault.
        var c = new ReceiverHealthClassifier(T());
        var dec = c.ClassifyDecoder(
            decodeRatioEma: 0.5,
            hangRateIn60s: 0,
            recoveryStreak: 0,
            presentSkipRatio: 0.9,
            receiverDecodePathDropRatio: 0);
        dec.Verdict.Should().NotBe(HealthVerdict.Bad);
        dec.PresentSkipRatio.Should().Be(0.9);
    }

    [Fact]
    public void DecodeRatioBad_AfterStreak_IsBad()
    {
        var c = new ReceiverHealthClassifier(T());
        DecoderHealth dec = DecoderHealth.Empty;
        for (var i = 0; i < 3; i++) {
            dec = c.ClassifyDecoder(
                decodeRatioEma: 2.0,
                hangRateIn60s: 0,
                recoveryStreak: 0,
                presentSkipRatio: 0,
                receiverDecodePathDropRatio: 0);
        }
        dec.Verdict.Should().Be(HealthVerdict.Bad);
    }
}
