using ActualChat.Video;

namespace ActualChat.Streaming;

public enum PlaybackQualityReason
{
    Stable = 0,
    Climb,
    Backoff,
    FloorReached,
    ActiveSetChanged,
    ReconnectPush,
    ColdStartTick,
}

public enum PlaybackStreamPriority
{
    Secondary = 0,
    Primary = 1,
}

/// <summary>
/// Per-stream playback sample plus the controller's classification.
/// Pruned to what the server-side allocator and telemetry consume.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record PlaybackStreamInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] long IncomingByteRate,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] double BufferDurationMsEma,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] PlaybackStreamPriority Priority,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] int Verdict);

/// <summary>
/// Aggregate playback health + per-stream details accompanying a playback
/// quality push. Drives capacity estimation and per-stream priority decisions
/// on the server's safety-cap step.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record PlaybackQualityInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] long EstimatedCapacityBytesPerSec,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] double AggregateHealth,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] PlaybackQualityReason Reason,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] bool IsColdStart,
    [property: DataMember(Order = 4), MemoryPackOrder(4), Key(4)] ApiMap<string, PlaybackStreamInfo> Streams);

/// <summary>
/// Per-tick playback stats — CLIENT-LOCAL only, never serialized to the
/// wire. Consumed by the local QC classifier (verdict + capacity estimator
/// + allocator) and the inbound-side video diagnostics. DropTrace is
/// aggregated at the present stage and covers recorder + server + receiver
/// stages (the trace bytes ride with every frame).
/// </summary>
public sealed record PlaybackStats(
    long IncomingByteRate,
    double BufferSpanMsEma,
    PlaybackStreamPriority Priority,
    int StreamAgeMs,
    bool QualityReductionRequested,
    double RenderCssLongSide,
    double RenderDevicePixelRatio,
    string Codec,
    // Cumulative per-stage drop counts since the playback run started.
    // Aggregated at the present operator, end-to-end (recorder + server + receiver).
    IReadOnlyDictionary<FrameDropStage, long> DropTrace,
    // Cumulative frames successfully presented to the render sink since the
    // run started. FPS = delta-per-second; powers the diagnostics row and
    // the optional FPS overlay.
    long Presented)
{
    public static PlaybackStats Empty { get; } =
        new(0, 0, PlaybackStreamPriority.Secondary, 0,
            QualityReductionRequested: false, 0, 0, "",
            EmptyDropTrace, 0);

    private static readonly IReadOnlyDictionary<FrameDropStage, long> EmptyDropTrace
        = new Dictionary<FrameDropStage, long>();

    public VideoSize RenderVideoSize
        => VideoSizeExt.FromLongSide(RenderCssLongSide, RenderDevicePixelRatio);
}
