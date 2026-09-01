using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record SenderHealthThresholds(
    // `EncodeDeficit*` thresholds match the THROUGHPUT-DEFICIT semantic of
    // `RecorderStats.EncodeDeficitEma` (range 0..1, 0 = encoder keeps up
    // with the frames offered to it, 1 = encoder emits nothing). Bad =
    // sustained deficit (fewer bundles than frames offered); Good = within a
    // few-% margin. Background allows a wider miss because the tab
    // itself is throttled and source rate is naturally lower.
    double EncodeDeficitBadForeground = 0.15,
    double EncodeDeficitBadBackground = 0.30,
    double EncodeDeficitGood = 0.03,
    // `EncodeQueueDepth*` is a SECONDARY signal — encoder's internal
    // queue depth EMA. Useful as an early-warning for impending
    // deficit but never the sole gate (a full queue with healthy
    // throughput is the desired pipelined state). Thresholds sized for
    // per-encoder maxInflight=5.
    double EncodeQueueDepthBad = 4.5,
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
        double encodeDeficitEma,
        double encodeQueueDepthEma,
        int restartStreakIn60s,
        double senderEncodePathDropRatio,
        bool isTabBackgrounded)
    {
        var encRatioBad = isTabBackgrounded
            ? _t.EncodeDeficitBadBackground
            : _t.EncodeDeficitBadForeground;
        var ratioVerdict = _encRatio.Update(_t.BadStreakRequired, _t.GoodStreakRequired,
            isBad: encodeDeficitEma > encRatioBad,
            isGood: encodeDeficitEma < _t.EncodeDeficitGood);
        var queueVerdict = _encQueue.Update(_t.BadStreakRequired, _t.GoodStreakRequired,
            isBad: encodeQueueDepthEma > _t.EncodeQueueDepthBad,
            isGood: encodeQueueDepthEma < _t.EncodeQueueDepthGood);
        var restartVerdict = ClassifyRestartStreak(restartStreakIn60s);
        var dropVerdict = senderEncodePathDropRatio > _t.SenderEncodePathDropRatioBad
            ? HealthVerdict.Bad
            : HealthVerdict.Good;
        var combined = HealthVerdictExt.Combine([ratioVerdict, queueVerdict, restartVerdict, dropVerdict]);
        var (badSignals, badFreeStreak) = combined != HealthVerdict.Bad
            ? ("", 0)
            : Attribute([
                ("deficit", ratioVerdict, _encRatio),
                ("encQueue", queueVerdict, _encQueue),
                ("restarts", restartVerdict, null),
                ("encDrop", dropVerdict, null),
            ]);
        return new EncoderHealth(combined,
            encodeDeficitEma, encodeQueueDepthEma, restartStreakIn60s,
            senderEncodePathDropRatio, isTabBackgrounded,
            badSignals, badFreeStreak, 2 * _t.GoodStreakRequired);
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
        var (badSignals, badFreeStreak) = combined != HealthVerdict.Bad
            ? ("", 0)
            : Attribute([
                ("ack", ackVerdict, _wireAck),
                ("queue", queueVerdict, _wireQueue),
                ("flood", floodVerdict, _floodSkip),
                ("reconnect", reconnectVerdict, null),
                ("drop", dropVerdict, null),
            ]);
        return new UplinkHealth(combined,
            wireLastAckAgeMs, wireQueueDepthEma, floodGateSkipPerSec,
            peerReconnectStreak, senderWirePathDropRatio,
            badSignals, badFreeStreak, 2 * _t.GoodStreakRequired);
    }

    // Private methods

    private static (string BadSignals, int BadFreeStreak) Attribute(
        ReadOnlySpan<(string Name, HealthVerdict Verdict, HealthStreakState? State)> signals)
    {
        // Names the Bad contributors and reports the latched signal furthest from
        // the bad-free decay (min BadFreeStreak); stateless signals report 0.
        var names = new List<string>();
        var badFreeStreak = int.MaxValue;
        foreach (var (name, verdict, state) in signals) {
            if (verdict != HealthVerdict.Bad)
                continue;

            names.Add(name);
            badFreeStreak = Math.Min(badFreeStreak, state?.BadFreeStreak ?? 0);
        }
        return (string.Join('+', names), badFreeStreak == int.MaxValue ? 0 : badFreeStreak);
    }

    private HealthVerdict ClassifyRestartStreak(int restartStreakIn60s)
        => restartStreakIn60s >= _t.RestartStreakBad ? HealthVerdict.Bad
            : restartStreakIn60s == 0 ? HealthVerdict.Good
            : HealthVerdict.Marginal;
}
