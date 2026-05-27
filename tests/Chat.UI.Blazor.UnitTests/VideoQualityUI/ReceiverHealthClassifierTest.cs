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
        DecodeDeficitBadStreak = 2,
        DecodeDeficitGoodStreak = 3,
    };

    [Fact]
    public void HighDownlinkLatency_Alone_DoesNotDriveDownlinkBad()
    {
        // Latency above-floor is diagnostic-only. With healthy throughput
        // (no drops, no underrun) Downlink should NOT be Bad.
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
                decodeDeficitEma: 0,
                hangRateIn60s: 0,
                recoveryStreak: 0,
                presentSkipRatio: 0,
                receiverDecodePathDropRatio: 0);
        }
        dl.Verdict.Should().NotBe(HealthVerdict.Bad);
        dl.ServerToReceiverLatencyEma.Should().Be(400);
        dec.Verdict.Should().Be(HealthVerdict.Good);
    }

    [Fact]
    public void HighDecodeDeficit_DrivesDecoderBad()
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
                decodeRatioEma: 0,
                decodeDeficitEma: 0.5,
                hangRateIn60s: 0,
                recoveryStreak: 0,
                presentSkipRatio: 0,
                receiverDecodePathDropRatio: 0);
        }
        dl.Verdict.Should().Be(HealthVerdict.Good);
        dec.Verdict.Should().Be(HealthVerdict.Bad);
    }

    [Fact]
    public void HighDecodeRatio_ZeroDeficit_DoesNotDriveBad()
    {
        // Regression: pipelined-decoder false-Bad. decodeRatioEma reflects
        // queue wait + decode CPU and inflates with pipeline depth; it must
        // not drive the verdict. Only decodeDeficitEma does.
        var c = new ReceiverHealthClassifier(T());
        var dec = c.ClassifyDecoder(
            decodeRatioEma: 7.0,
            decodeDeficitEma: 0,
            hangRateIn60s: 0,
            recoveryStreak: 0,
            presentSkipRatio: 0,
            receiverDecodePathDropRatio: 0);
        dec.Verdict.Should().NotBe(HealthVerdict.Bad);
    }

    [Fact]
    public void DecoderHang_OneEvent_PromotesDecoderBad()
    {
        var c = new ReceiverHealthClassifier(T());
        var dec = c.ClassifyDecoder(
            decodeRatioEma: 0.5,
            decodeDeficitEma: 0,
            hangRateIn60s: 1,
            recoveryStreak: 0,
            presentSkipRatio: 0,
            receiverDecodePathDropRatio: 0);
        dec.Verdict.Should().Be(HealthVerdict.Bad);
    }

    [Fact]
    public void RecoveryStreak_AtThreshold_DoesNotDriveBad()
    {
        var c = new ReceiverHealthClassifier(T());
        var dec = c.ClassifyDecoder(
            decodeRatioEma: 0.5,
            decodeDeficitEma: 0,
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
    public void PresentSkipHigh_NoOtherSignal_DoesNotDriveBad()
    {
        var c = new ReceiverHealthClassifier(T());
        var dec = c.ClassifyDecoder(
            decodeRatioEma: 0.5,
            decodeDeficitEma: 0,
            hangRateIn60s: 0,
            recoveryStreak: 0,
            presentSkipRatio: 0.9,
            receiverDecodePathDropRatio: 0);
        dec.Verdict.Should().NotBe(HealthVerdict.Bad);
        dec.PresentSkipRatio.Should().Be(0.9);
    }

    [Fact]
    public void DecodeDeficit_AfterStreak_IsBad()
    {
        var t = T();
        var c = new ReceiverHealthClassifier(t);
        DecoderHealth dec = DecoderHealth.Empty;
        for (var i = 0; i < t.DecodeDeficitBadStreak; i++) {
            dec = c.ClassifyDecoder(
                decodeRatioEma: 0,
                decodeDeficitEma: 0.5,
                hangRateIn60s: 0,
                recoveryStreak: 0,
                presentSkipRatio: 0,
                receiverDecodePathDropRatio: 0);
        }
        dec.Verdict.Should().Be(HealthVerdict.Bad);
    }

    [Fact]
    public void LowDecodeDeficit_IsGood()
    {
        // Sustained deficit below the Good threshold (0.03 default) keeps
        // the decoder Good no matter how long the run.
        var c = new ReceiverHealthClassifier(T());
        DecoderHealth dec = DecoderHealth.Empty;
        for (var i = 0; i < 5; i++) {
            dec = c.ClassifyDecoder(
                decodeRatioEma: 0,
                decodeDeficitEma: 0.01,
                hangRateIn60s: 0,
                recoveryStreak: 0,
                presentSkipRatio: 0,
                receiverDecodePathDropRatio: 0);
        }
        dec.Verdict.Should().Be(HealthVerdict.Good);
    }

    [Fact]
    public void LowByteRateDeficit_Alone_DoesNotDriveDownlinkBad()
    {
        // incomingByteRateDeficit is diagnostic-only. Efficient codecs and
        // static scenes legitimately produce < expected ladder rate while
        // delivering all frames; that's not a downlink problem.
        var c = new ReceiverHealthClassifier(T());
        DownlinkHealth dl = DownlinkHealth.Empty;
        for (var i = 0; i < 5; i++) {
            dl = c.ClassifyDownlink(
                serverToReceiverLatencyEma: 20,
                arrivalIntervalEma: 33,
                serverPathDropRatio: 0,
                bufferUnderrunRatio: 0,
                incomingByteRateDeficit: 0.3);
        }
        dl.Verdict.Should().NotBe(HealthVerdict.Bad);
        dl.IncomingByteRateDeficit.Should().Be(0.3);
    }
}
