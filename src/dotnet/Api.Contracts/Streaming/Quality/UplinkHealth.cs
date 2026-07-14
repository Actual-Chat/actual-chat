namespace ActualChat.Streaming;

// Uplink-leg verdict: sender→server wire conditions (ACK staleness, queue
// backpressure, FloodGate skips, peer reconnects). Drives sender
// BandwidthCap only.
public sealed record UplinkHealth(
    HealthVerdict Verdict,
    double WireLastAckAgeMs,
    double WireQueueDepthEma,
    double FloodGateSkipPerSec,
    int PeerReconnectStreak,
    double SenderWirePathDropRatio,
    // Comma-joined names of the signals whose verdict equals the combined
    // Bad verdict ("" when not Bad) — the diagnostics attribution.
    string BadSignals = "",
    // Bad-free tick count / recovery target of the latched Bad signal
    // furthest from decay (0/0 when not applicable).
    int BadFreeStreak = 0,
    int BadRecoverAtStreak = 0)
{
    public static UplinkHealth Empty { get; } =
        new(HealthVerdict.Unknown, 0, 0, 0, 0, 0);
}
