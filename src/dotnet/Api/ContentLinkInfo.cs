using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ContentLinkInfo(
    [property: DataMember, MemoryPackOrder(0)] ContentId Id,
    [property: DataMember, MemoryPackOrder(1)] string Title,
    [property: DataMember, MemoryPackOrder(2)] Picture? Picture,
    [property: DataMember, MemoryPackOrder(3)] string Description)
{
    public static ContentLinkInfo RemovedOrUnknown(ContentId id)
        => new (id, "Removed or Unknown", null, "");
}
