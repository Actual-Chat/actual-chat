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
    double SenderWirePathDropRatio)
{
    public static UplinkHealth Empty { get; } =
        new(HealthVerdict.Unknown, 0, 0, 0, 0, 0);
}
