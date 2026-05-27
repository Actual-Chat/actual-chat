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
    // DecodeDeficit = 1 - decoded/arrived. 0 = decoder keeps pace with source.
    // 0.10 = decoder loses >10% of arrived chunks sustained → Bad.
    // 0.03 = within tolerance → Good. Mirrors the encoder-side EncodeDeficit
    // semantics so both QC legs read on the same scale.
    double DecodeDeficitBad = 0.10,
    double DecodeDeficitGood = 0.03,
    int HangRateBad = 1,
    int RecoveryStreakBad = 2,
    double PresentSkipRatioBad = 0.3,
    double ReceiverDecodePathDropRatioBad = 0.1,
    int DownlinkLatencyBadStreak = 2,
    int DownlinkLatencyGoodStreak = 5,
    int ByteRateDeficitBadStreak = 3,
    int ByteRateDeficitGoodStreak = 5,
    int DecodeDeficitBadStreak = 2,
    int DecodeDeficitGoodStreak = 5)
{
    public static ReceiverHealthThresholds Defaults { get; } = new();
}

public sealed class ReceiverHealthClassifier(ReceiverHealthThresholds? thresholds = null)
{
    private readonly ReceiverHealthThresholds _t = thresholds ?? ReceiverHealthThresholds.Defaults;
    private readonly HealthStreakState _underrun = new();
    private readonly HealthStreakState _decodeDeficit = new();

    public DownlinkHealth ClassifyDownlink(
        double serverToReceiverLatencyEma,
        double arrivalIntervalEma,
        double serverPathDropRatio,
        double bufferUnderrunRatio,
        double incomingByteRateDeficit)
    {
        var underrunVerdict = _underrun.Update(
            _t.DownlinkLatencyBadStreak, _t.DownlinkLatencyGoodStreak,
            isBad: bufferUnderrunRatio > _t.BufferUnderrunRatioBad,
            isGood: bufferUnderrunRatio < _t.BufferUnderrunRatioGood);
        var dropVerdict = serverPathDropRatio > _t.ServerPathDropRatioBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        // Downlink combine includes ONLY direct delivery-failure signals:
        // underrun (buffer running dry) and drops (frames lost on server→
        // receiver leg). Excluded by design:
        //  - latency above-floor: high RTT with healthy throughput doesn't
        //    justify lowering bitrate.
        //  - byte-rate deficit: actual-vs-predicted is a measurement
        //    artifact of codec/scene variance, not a delivery problem.
        // Both raw values stay on the DownlinkHealth record for diagnostics.
        var combined = HealthVerdictExt.Combine([underrunVerdict, dropVerdict]);
        return new DownlinkHealth(combined,
            serverToReceiverLatencyEma, arrivalIntervalEma, serverPathDropRatio,
            bufferUnderrunRatio, incomingByteRateDeficit);
    }

    public DecoderHealth ClassifyDecoder(
        double decodeRatioEma,
        double decodeDeficitEma,
        int hangRateIn60s,
        int recoveryStreak,
        double presentSkipRatio,
        double receiverDecodePathDropRatio)
    {
        var deficitVerdict = _decodeDeficit.Update(
            _t.DecodeDeficitBadStreak, _t.DecodeDeficitGoodStreak,
            isBad: decodeDeficitEma > _t.DecodeDeficitBad,
            isGood: decodeDeficitEma < _t.DecodeDeficitGood);
        var hangVerdict = hangRateIn60s >= _t.HangRateBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        var dropVerdict = receiverDecodePathDropRatio > _t.ReceiverDecodePathDropRatioBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        // decodeRatioEma / recoveryStreak / presentSkipRatio remain on the record
        // for diagnostics but no longer drive the verdict. decodeRatioEma is biased
        // by pipeline depth (it conflates per-frame work with queue wait); decoder
        // throughput is the QC signal, sourced from chunksReceived vs framesDecoded.
        var combined = HealthVerdictExt.Combine([deficitVerdict, hangVerdict, dropVerdict]);
        return new DecoderHealth(combined,
            decodeRatioEma, decodeDeficitEma, hangRateIn60s, recoveryStreak,
            presentSkipRatio, receiverDecodePathDropRatio);
    }
}
