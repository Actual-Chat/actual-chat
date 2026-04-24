namespace ActualChat;

/// <summary>
/// Metadata for a content link including title, picture, and description.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ContentLinkInfo(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ContentId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Title,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Picture? Picture,
    [property: DataMember, MemoryPackOrder(3), Key(3)] string Description)
{
    public static ContentLinkInfo RemovedOrUnknown(ContentId id)
        => new (id, "Removed or Unknown", null, "");
}
