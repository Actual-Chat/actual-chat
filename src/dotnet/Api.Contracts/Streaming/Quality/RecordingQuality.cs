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
/// Recorder controller's intended simulcast layer count and the count actually
/// applied to the encoder. Sent to the server purely as a metric.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record RecordingQualityState(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int TargetLayerCount,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int EffectiveLayerCount);

/// <summary>
/// Per-second snapshot of recorder-side health signals: encode cost vs frame
/// budget, slot pressure, sender backlog, and ACK age. Drives the recording
/// quality controller; also forwarded to the server as a metric.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record RecorderHealthSnapshot(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] double EncodeRatioAvg,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] double EncodeRatioP90,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] double SlotReplacementRate,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] double SenderBacklogP90Ms,
    [property: DataMember(Order = 4), MemoryPackOrder(4), Key(4)] int SenderSkipsPerWindow,
    [property: DataMember(Order = 5), MemoryPackOrder(5), Key(5)] double LastAckAgeMs,
    [property: DataMember(Order = 6), MemoryPackOrder(6), Key(6)] bool IsConnected);

/// <summary>
/// Diagnostic payload accompanying a recording quality decision: the reason
/// the controller chose this state and the health snapshot it acted on.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record RecordingQualityInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] RecordingQualityReason Reason,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] RecorderHealthSnapshot Health);
