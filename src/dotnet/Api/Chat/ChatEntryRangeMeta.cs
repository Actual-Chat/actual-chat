using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatEntryRangeMeta(
    [property: DataMember, MemoryPackOrder(0)] ChatId? ChatId,
    [property: DataMember, MemoryPackOrder(1)] Range<long>[] EntryRanges,
    [property: DataMember, MemoryPackOrder(2)] long? PreviousEntryId,
    [property: DataMember, MemoryPackOrder(3)] long? NextEntryId)
{
    public static readonly ChatEntryRangeMeta None = new(null, [], null, null);
}
