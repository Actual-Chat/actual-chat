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
/// Per-second snapshot of recorder-side health signals: encode cost vs frame
/// budget, slot pressure, sender drops, and ACK age. Drives the recording
/// quality controller; also forwarded to the server as a metric.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record RecorderHealthSnapshot(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] double EncodeRatioEma,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] double EncodeRatioP90,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] double SlotReplacementRateEma,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] double SenderFrameDropRatioEma,
    [property: DataMember(Order = 4), MemoryPackOrder(4), Key(4)] double LastAckAgeMs,
    [property: DataMember(Order = 5), MemoryPackOrder(5), Key(5)] bool IsConnected,
    [property: DataMember(Order = 6), MemoryPackOrder(6), Key(6)] bool IsPeerConnected = true,
    // Order 7 was SenderFramesDropped; now repurposed as FloodGateSkipCount —
    // capture-side drops while push-to-pull-buffer was full. Numerically
    // compatible with the previous "frames dropped at the sender queue" field
    // (which fell to ~0 in practice once VersionTolerant clients deployed).
    [property: DataMember(Order = 7), MemoryPackOrder(7), Key(7)] long FloodGateSkipCount = 0,
    // Order 8 (was SenderKeyframesDropped) is removed — the flood gate
    // operates on raw captured frames before keyframe policy runs, so there's
    // no per-keyframe count to report. VersionTolerant clients tolerate gaps.
    [property: DataMember(Order = 9), MemoryPackOrder(9), Key(9)] long RpcStreamFramesSkipped = 0,
    [property: DataMember(Order = 10), MemoryPackOrder(10), Key(10)] int SenderQueueDepth = 0,
    [property: DataMember(Order = 11), MemoryPackOrder(11), Key(11)] int SenderMaxQueueDepth = 0);

/// <summary>
/// Diagnostic payload accompanying a recording quality decision: the reason
/// the controller chose this state and the health snapshot it acted on.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record RecordingQualityInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] RecordingQualityReason Reason,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] RecorderHealthSnapshot Health);
