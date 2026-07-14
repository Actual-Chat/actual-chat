namespace ActualChat.Streaming;

// Encoder-leg verdict: sender machine encode time, codec restarts, sender
// encode-stage drops. Drives EncodingCap only. Tab-backgrounded relaxes the
// encode-ratio threshold to accommodate Chrome's hidden-tab throttling.
public sealed record EncoderHealth(
    HealthVerdict Verdict,
    double EncodeDeficitEma,
    double EncodeQueueDepthEma,
    int RestartStreakIn60s,
    double SenderEncodePathDropRatio,
    bool IsTabBackgrounded,
    // Comma-joined names of the signals whose verdict equals the combined
    // Bad verdict ("" when not Bad) — the diagnostics attribution.
    string BadSignals = "",
    // Bad-free tick count / recovery target of the latched Bad signal
    // furthest from decay (0/0 when not applicable).
    int BadFreeStreak = 0,
    int BadRecoverAtStreak = 0)
{
    public static EncoderHealth Empty { get; } =
        new(HealthVerdict.Unknown, 0, 0, 0, 0, false);
}
