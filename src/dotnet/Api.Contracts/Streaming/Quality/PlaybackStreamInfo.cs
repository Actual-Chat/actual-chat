namespace ActualChat.Streaming;

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
