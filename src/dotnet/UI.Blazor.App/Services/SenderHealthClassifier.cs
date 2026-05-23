using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record SenderHealthThresholds(
    // `EncodeRatio*` thresholds were retuned when the encode operator
    // gained per-encoder pipelining: the metric is now queue-saturation
    // (encodeQueueDepthEma / maxInflight, range 0..1), NOT wall-clock
    // per-bundle / frameDuration. Bad = sustained heavy saturation,
    // Good = encoder mostly idle. Background allows higher saturation
    // because the tab itself is throttled.
    double EncodeRatioBadForeground = 0.6,
    double EncodeRatioBadBackground = 0.9,
    double EncodeRatioGood = 0.2,
    double EncodeQueueDepthBad = 3.5,
    double EncodeQueueDepthGood = 1.0,
    int RestartStreakBad = 2,
    double SenderEncodePathDropRatioBad = 0.1,
    double WireLastAckAgeBadMs = 2_000,
    double WireLastAckAgeGoodMs = 500,
    double WireQueueDepthBad = 4,
    double WireQueueDepthGood = 1,
    double FloodGateSkipPerSecBad = 0.5,
    int PeerReconnectStreakBad = 1,
    double SenderWirePathDropRatioBad = 0.1,
    int BadStreakRequired = 2,
    int GoodStreakRequired = 5)
{
    public static SenderHealthThresholds Defaults { get; } = new();
}

// Stateful classifier — owns per-signal hysteresis counters across ticks.
// One instance per recording run. Pure C#: no I/O, no logging, no clock.
public sealed class SenderHealthClassifier(SenderHealthThresholds? thresholds = null)
{
    private readonly SenderHealthThresholds _t = thresholds ?? SenderHealthThresholds.Defaults;
    private readonly HealthStreakState _encRatio = new();
    private readonly HealthStreakState _encQueue = new();
    private readonly HealthStreakState _wireAck = new();
    private readonly HealthStreakState _wireQueue = new();
    private readonly HealthStreakState _floodSkip = new();

    public EncoderHealth ClassifyEncoder(
        double encodeRatioEma,
        double encodeQueueDepthEma,
        int restartStreakIn60s,
        double senderEncodePathDropRatio,
        bool isTabBackgrounded)
    {
        var encRatioBad = isTabBackgrounded
            ? _t.EncodeRatioBadBackground
            : _t.EncodeRatioBadForeground;
        var ratioVerdict = _encRatio.Update(_t.BadStreakRequired, _t.GoodStreakRequired,
            isBad: encodeRatioEma > encRatioBad,
            isGood: encodeRatioEma < _t.EncodeRatioGood);
        var queueVerdict = _encQueue.Update(_t.BadStreakRequired, _t.GoodStreakRequired,
            isBad: encodeQueueDepthEma > _t.EncodeQueueDepthBad,
            isGood: encodeQueueDepthEma < _t.EncodeQueueDepthGood);
        var restartVerdict = ClassifyRestartStreak(restartStreakIn60s);
        var dropVerdict = senderEncodePathDropRatio > _t.SenderEncodePathDropRatioBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        var combined = HealthVerdictExt.Combine([ratioVerdict, queueVerdict, restartVerdict, dropVerdict]);
        return new EncoderHealth(combined,
            encodeRatioEma, encodeQueueDepthEma, restartStreakIn60s,
            senderEncodePathDropRatio, isTabBackgrounded);
    }

    public UplinkHealth ClassifyUplink(
        double wireLastAckAgeMs,
        double wireQueueDepthEma,
        double floodGateSkipPerSec,
        int peerReconnectStreak,
        double senderWirePathDropRatio)
    {
        var ackVerdict = _wireAck.Update(_t.BadStreakRequired, _t.GoodStreakRequired,
            isBad: wireLastAckAgeMs > _t.WireLastAckAgeBadMs,
            isGood: wireLastAckAgeMs < _t.WireLastAckAgeGoodMs);
        var queueVerdict = _wireQueue.Update(_t.BadStreakRequired, _t.GoodStreakRequired,
            isBad: wireQueueDepthEma > _t.WireQueueDepthBad,
            isGood: wireQueueDepthEma < _t.WireQueueDepthGood);
        var floodVerdict = _floodSkip.Update(_t.BadStreakRequired, _t.GoodStreakRequired,
            isBad: floodGateSkipPerSec > _t.FloodGateSkipPerSecBad,
            isGood: floodGateSkipPerSec == 0);
        var reconnectVerdict = peerReconnectStreak >= _t.PeerReconnectStreakBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        var dropVerdict = senderWirePathDropRatio > _t.SenderWirePathDropRatioBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        var combined = HealthVerdictExt.Combine([ackVerdict, queueVerdict, floodVerdict, reconnectVerdict, dropVerdict]);
        return new UplinkHealth(combined,
            wireLastAckAgeMs, wireQueueDepthEma, floodGateSkipPerSec,
            peerReconnectStreak, senderWirePathDropRatio);
    }

    private HealthVerdict ClassifyRestartStreak(int restartStreakIn60s)
        => restartStreakIn60s >= _t.RestartStreakBad ? HealthVerdict.Bad
            : restartStreakIn60s == 0 ? HealthVerdict.Good
            : HealthVerdict.Marginal;
}
