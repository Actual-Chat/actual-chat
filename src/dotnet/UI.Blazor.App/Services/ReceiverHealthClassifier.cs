using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record ReceiverHealthThresholds(
    double ServerToReceiverLatencyBadMs = 250,
    double ServerToReceiverLatencyGoodMs = 80,
    double IncomingByteRateDeficitBad = 0.6,
    double IncomingByteRateDeficitGood = 0.9,
    double BufferUnderrunRatioBad = 0.30,
    double BufferUnderrunRatioGood = 0.05,
    double ServerPathDropRatioBad = 0.1,
    double DecodeRatioBad = 1.5,
    double DecodeRatioGood = 0.8,
    int HangRateBad = 1,
    int RecoveryStreakBad = 2,
    double PresentSkipRatioBad = 0.3,
    double ReceiverDecodePathDropRatioBad = 0.1,
    int DownlinkLatencyBadStreak = 2,
    int DownlinkLatencyGoodStreak = 5,
    int ByteRateDeficitBadStreak = 3,
    int ByteRateDeficitGoodStreak = 5,
    int DecodeRatioBadStreak = 2,
    int DecodeRatioGoodStreak = 5)
{
    public static ReceiverHealthThresholds Defaults { get; } = new();
}

// Stateful per-stream classifier. The receiver decomposes the legacy fused
// drift signal by reading DecodeRatioEma alongside ServerToReceiverLatencyEma:
// drift unexplained by decode defaults to Downlink (see plan §"Attribution").
public sealed class ReceiverHealthClassifier(ReceiverHealthThresholds? thresholds = null)
{
    private readonly ReceiverHealthThresholds _t = thresholds ?? ReceiverHealthThresholds.Defaults;
    private readonly HealthStreakState _latency = new();
    private readonly HealthStreakState _byteRateDeficit = new();
    private readonly HealthStreakState _underrun = new();
    private readonly HealthStreakState _decodeRatio = new();

    public DownlinkHealth ClassifyDownlink(
        double serverToReceiverLatencyEma,
        double arrivalIntervalEma,
        double serverPathDropRatio,
        double bufferUnderrunRatio,
        double incomingByteRateDeficit)
    {
        var latencyVerdict = _latency.Update(
            _t.DownlinkLatencyBadStreak, _t.DownlinkLatencyGoodStreak,
            isBad: serverToReceiverLatencyEma > _t.ServerToReceiverLatencyBadMs,
            isGood: serverToReceiverLatencyEma < _t.ServerToReceiverLatencyGoodMs);
        var deficitVerdict = _byteRateDeficit.Update(
            _t.ByteRateDeficitBadStreak, _t.ByteRateDeficitGoodStreak,
            isBad: incomingByteRateDeficit >= 0 && incomingByteRateDeficit < _t.IncomingByteRateDeficitBad,
            isGood: incomingByteRateDeficit > _t.IncomingByteRateDeficitGood);
        var underrunVerdict = _underrun.Update(
            _t.DownlinkLatencyBadStreak, _t.DownlinkLatencyGoodStreak,
            isBad: bufferUnderrunRatio > _t.BufferUnderrunRatioBad,
            isGood: bufferUnderrunRatio < _t.BufferUnderrunRatioGood);
        var dropVerdict = serverPathDropRatio > _t.ServerPathDropRatioBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        var combined = HealthVerdictExt.Combine([latencyVerdict, deficitVerdict, underrunVerdict, dropVerdict]);
        return new DownlinkHealth(combined,
            serverToReceiverLatencyEma, arrivalIntervalEma, serverPathDropRatio,
            bufferUnderrunRatio, incomingByteRateDeficit);
    }

    public DecoderHealth ClassifyDecoder(
        double decodeRatioEma,
        int hangRateIn60s,
        int recoveryStreak,
        double presentSkipRatio,
        double receiverDecodePathDropRatio)
    {
        var ratioVerdict = _decodeRatio.Update(
            _t.DecodeRatioBadStreak, _t.DecodeRatioGoodStreak,
            isBad: decodeRatioEma > _t.DecodeRatioBad,
            isGood: decodeRatioEma < _t.DecodeRatioGood);
        var hangVerdict = hangRateIn60s >= _t.HangRateBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        var recoveryVerdict = recoveryStreak >= _t.RecoveryStreakBad
            ? HealthVerdict.Bad
            : recoveryStreak == 0 ? HealthVerdict.Good
            : HealthVerdict.Marginal;
        var skipVerdict = presentSkipRatio > _t.PresentSkipRatioBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        var dropVerdict = receiverDecodePathDropRatio > _t.ReceiverDecodePathDropRatioBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        var combined = HealthVerdictExt.Combine([ratioVerdict, hangVerdict, recoveryVerdict, skipVerdict, dropVerdict]);
        return new DecoderHealth(combined,
            decodeRatioEma, hangRateIn60s, recoveryStreak,
            presentSkipRatio, receiverDecodePathDropRatio);
    }
}
