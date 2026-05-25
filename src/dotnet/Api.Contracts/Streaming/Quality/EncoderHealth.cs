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
    bool IsTabBackgrounded)
{
    public static EncoderHealth Empty { get; } =
        new(HealthVerdict.Unknown, 0, 0, 0, 0, false);
}
