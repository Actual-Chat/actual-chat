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
    int AvailableTemporalLayerCount,
    double RenderCssLongSide,
    double RenderDevicePixelRatio,
    int PresentedCount,
    int StreamDurationMs,
    long IncomingByteRate,
    double BufferSpanMsEma,
    double PlaybackRateEma,
    IReadOnlyDictionary<FrameDropStage, int> DropTrace)
{
    private static readonly IReadOnlyDictionary<FrameDropStage, int> EmptyDropTrace
        = new Dictionary<FrameDropStage, int>();

    public static PlaybackStats Empty { get; } =
        new(PlaybackStreamPriority.Secondary, "", 1,
            0, 0, // Render*
            PresentedCount: 0,
            StreamDurationMs: 0,
            IncomingByteRate: 0,
            BufferSpanMsEma: 0,
            PlaybackRateEma: 1,
            DropTrace: EmptyDropTrace);

    public VideoSize RenderVideoSize
        => VideoSizeExt.FromLongSide(RenderCssLongSide, RenderDevicePixelRatio);
}
