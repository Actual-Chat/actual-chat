using ActualChat.Video;

namespace ActualChat.Streaming;

public enum RecordingQualityReason
{
    Stable = 0,
    Climb,
    Backoff,
    StuckAtFloor,
    ColdStartTick,
    ReconnectPush,
}

/// <summary>
/// Recorder controller's intended layer count and the count actually
/// applied to the encoder. Sent to the server purely as a metric.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record RecordingQualityState(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int TargetLayerCount,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int EffectiveLayerCount);

/// <summary>
/// Wire payload accompanying a recording quality decision. Slim: only
/// what the server-side telemetry uses (VideoSendDropRatio, VideoSendAckAgeMs).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record RecordingQualityInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] RecordingQualityReason Reason,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] double SenderFrameDropRatioEma,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] double LastAckAgeMs);

/// <summary>
/// Per-tick recorder stats — CLIENT-LOCAL only, never serialized to the
/// wire. Consumed by the local QC classifier and the outbound-side video
/// diagnostics. Pruned to only what one of those two consumers reads.
/// </summary>
public sealed record RecorderStats(
    // Encode cost relative to frame budget. Computed as
    // (sum of all per-layer encode times) / frameDuration, EMA-smoothed.
    double EncodeRatioEma,
    // Bundles dropped anywhere in the sender pipeline / (bundles dropped +
    // bundles shipped), EMA-smoothed.
    double SenderFrameDropRatioEma,
    double LastAckAgeMs,
    bool IsConnected,
    bool IsPeerConnected,
    // Cumulative per-stage drop counts since the recording run started.
    // Powers the per-stream FPS breakdown in video diagnostics.
    IReadOnlyDictionary<FrameDropStage, int> DropTrace,
    // Cumulative bundles successfully shipped to the wire since the run
    // started. Powers FPS (= delta-per-second) for the diagnostics row.
    int BundlesShipped,
    // Cumulative encoded bytes (sum across layers). Drives the outbound
    // "kbps" display in the diagnostics modal.
    long BytesEncoded,
    // Health-classifier inputs (Step 4 of split-attribution QC plan).
    double EncodeQueueDepthEma,
    double WireQueueDepthEma,
    double FloodGateSkipPerSec,
    int PeerReconnectStreak,
    int EncoderRestartStreakIn60s,
    bool IsTabBackgrounded)
{
    private static readonly IReadOnlyDictionary<FrameDropStage, int> EmptyDropTrace
        = new Dictionary<FrameDropStage, int>();

    public static RecorderStats Empty { get; } =
        new(0, 0, 0, IsConnected: false, IsPeerConnected: false, EmptyDropTrace, 0, 0,
            EncodeQueueDepthEma: -1,
            WireQueueDepthEma: -1,
            FloodGateSkipPerSec: 0,
            PeerReconnectStreak: 0,
            EncoderRestartStreakIn60s: 0,
            IsTabBackgrounded: false);
}
