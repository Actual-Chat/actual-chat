namespace ActualChat.Streaming;

/// <summary>
/// The viewer-demand aggregate for one stream, as seen by the stream's owning
/// node. Functional decision inputs only — provenance counters live in
/// <see cref="StreamDemandStats"/> (pull-only, so their churn can't drive
/// invalidations).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record StreamDemandInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int Mask,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] bool ThumbnailViewersOnly)
{
    public static readonly StreamDemandInfo None = new(0, false);
}

/// <summary>
/// Demand provenance for diagnostics: who is reporting demand for a stream.
/// Served by a plain (non-compute) method and polled by the diagnostics UI —
/// deliberately NOT part of <see cref="StreamDemandInfo"/>, since the counters
/// change on every viewer join/leave/pause and would invalidate the demand
/// computes far more often than the decision inputs actually change.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record StreamDemandStats(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int ViewerCount,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int PausedCount);
