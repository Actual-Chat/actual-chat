namespace ActualChat.Streaming;

// Downlink-leg verdict: server→receiver path (server fan-out + wire).
// ServerToReceiverLatencyEma is above-floor latency from a sliding-min
// skew baseline (raw - min). Drives per-stream ReceiveQuality only.
public sealed record DownlinkHealth(
    HealthVerdict Verdict,
    double ServerToReceiverLatencyEma,
    double ArrivalIntervalEma,
    double ServerPathDropRatio,
    double BufferUnderrunRatio,
    double IncomingByteRateDeficit)
{
    public static DownlinkHealth Empty { get; } =
        new(HealthVerdict.Unknown, 0, 0, 0, 0, 0);
}
