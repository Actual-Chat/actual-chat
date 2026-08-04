namespace ActualChat.Streaming;

/// <summary>
/// Aggregate playback health + per-stream details accompanying a playback
/// quality push. Drives capacity estimation and per-stream priority decisions
/// on the server's safety-cap step.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PlaybackQualityInfo(
    [property: DataMember(Order = 0), Key(0)] long EstimatedCapacityBytesPerSec,
    [property: DataMember(Order = 1), Key(1)] double AggregateHealth,
    [property: DataMember(Order = 2), Key(2)] PlaybackQualityReason Reason,
    [property: DataMember(Order = 3), Key(3)] bool IsColdStart,
    [property: DataMember(Order = 4), Key(4)] ApiMap<string, PlaybackStreamInfo> Streams,
    // Non-empty only when the receiver detected a playback stall. Carries the
    // stalled stream ids + trigger, since the client console isn't collectable.
    [property: DataMember(Order = 5), Key(5)] string StallNote = "");
