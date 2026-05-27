namespace ActualChat.Streaming;

// Decoder-leg verdict: receiver-machine WebCodecs decoder health. Drives a
// per-stream decoder layer cap (separate from downlink cap); affects only
// this receiver, never the sender ladder.
public sealed record DecoderHealth(
    HealthVerdict Verdict,
    double DecodeRatioEma,
    double DecodeDeficitEma,
    int HangRateIn60s,
    int RecoveryStreak,
    double PresentSkipRatio,
    double ReceiverDecodePathDropRatio)
{
    public static DecoderHealth Empty { get; } =
        new(HealthVerdict.Unknown, 0, 0, 0, 0, 0, 0);
}
