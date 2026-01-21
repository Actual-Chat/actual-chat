using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ConversationRangeMeta(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] Range<long>[] ConversationRanges,
    [property: DataMember, MemoryPackOrder(2)] Range<long>? PreviousConversationRange,
    [property: DataMember, MemoryPackOrder(3)] Range<long>? NextConversationRange)
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ConversationId[] ConversationIds
        => field ??= ConversationRanges.Select(r => ConversationId.New(ChatId, r.Start)).ToArray();
}
