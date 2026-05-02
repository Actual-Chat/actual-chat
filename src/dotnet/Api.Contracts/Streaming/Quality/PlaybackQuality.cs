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
/// Per-stream playback health sample plus the controller's classification:
/// rate, buffer, decoder load, currently-applied caps, and a -1/0/+1 verdict.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record PlaybackStreamInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] long IncomingByteRate,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int BufferDurationMsP50,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] int KeyframeSkipsInWindow,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] int DecoderQueueDepthP90,
    [property: DataMember(Order = 4), MemoryPackOrder(4), Key(4)] int CurrentMaxSpatial,
    [property: DataMember(Order = 5), MemoryPackOrder(5), Key(5)] int CurrentMaxTemporal,
    [property: DataMember(Order = 6), MemoryPackOrder(6), Key(6)] PlaybackStreamPriority Priority,
    [property: DataMember(Order = 7), MemoryPackOrder(7), Key(7)] int Verdict);

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
