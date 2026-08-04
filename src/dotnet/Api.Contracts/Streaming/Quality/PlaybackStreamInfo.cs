namespace ActualChat.Streaming;

/// <summary>
/// Per-stream playback sample plus the controller's classification.
/// Pruned to what the server-side allocator and telemetry consume.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PlaybackStreamInfo(
    [property: DataMember(Order = 0), Key(0)] long IncomingByteRate,
    [property: DataMember(Order = 1), Key(1)] double BufferDurationMsEma,
    [property: DataMember(Order = 2), Key(2)] PlaybackStreamPriority Priority,
    [property: DataMember(Order = 3), Key(3)] int Verdict);
