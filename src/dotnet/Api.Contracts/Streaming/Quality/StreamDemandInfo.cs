namespace ActualChat.Streaming;

/// <summary>
/// Aggregated viewer demand for one stream, as seen by the stream's owning
/// node: the layer bitmask plus who is reporting — so an empty mask is
/// attributable (no reports vs. everyone paused).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record StreamDemandInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int Mask,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int ViewerCount,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] int PausedCount,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] bool ThumbnailViewersOnly)
{
    public static readonly StreamDemandInfo None = new(0, 0, 0, false);
}
