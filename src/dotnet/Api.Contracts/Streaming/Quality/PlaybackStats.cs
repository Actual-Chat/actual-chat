using ActualChat.Video;

namespace ActualChat.Streaming;

/// <summary>
/// Per-tick playback stats — CLIENT-LOCAL only, never serialized to the
/// wire. Consumed by the local QC classifier (verdict + capacity estimator
/// + allocator) and the inbound-side video diagnostics. DropTrace is
/// aggregated at the present stage and covers recorder + server + receiver
/// stages (the trace bytes ride with every frame).
/// </summary>
public sealed record PlaybackStats(
    PlaybackStreamPriority Priority,
    string Codec,
    double RenderCssLongSide,
    double RenderDevicePixelRatio,
    int PresentedCount,
    int StreamDurationMs,
    long IncomingByteRate,
    double BufferSpanMsEma,
    double PlaybackRateEma,
    IReadOnlyDictionary<FrameDropStage, int> DropTrace,
    // Receiver health-classifier inputs (Steps 2-3 of split-attribution
    // QC plan). -1 sentinels mean "not yet sampled".
    double DecodeRatioEma,
    int HangRateIn60s,
    int RecoveryStreak,
    double PresentSkipRatio,
    double BufferUnderrunRatio,
    double DownlinkLatencyEma,
    double ArrivalIntervalEma)
{
    private static readonly IReadOnlyDictionary<FrameDropStage, int> EmptyDropTrace
        = new Dictionary<FrameDropStage, int>();

    public static PlaybackStats Empty { get; } =
        new(PlaybackStreamPriority.Secondary, "",
            0, 0, // Render*
            PresentedCount: 0,
            StreamDurationMs: 0,
            IncomingByteRate: 0,
            BufferSpanMsEma: 0,
            PlaybackRateEma: 1,
            DropTrace: EmptyDropTrace,
            DecodeRatioEma: -1,
            HangRateIn60s: 0,
            RecoveryStreak: 0,
            PresentSkipRatio: -1,
            BufferUnderrunRatio: -1,
            DownlinkLatencyEma: -1,
            ArrivalIntervalEma: -1);

    public VideoSize RenderVideoSize
        => VideoSizeExt.FromLongSide(RenderCssLongSide, RenderDevicePixelRatio);
}
