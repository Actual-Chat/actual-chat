using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ConversationRangeMeta(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] Range<long>[] ConversationRanges,
    [property: DataMember, MemoryPackOrder(2)] Range<long>? PreviousConversationRange,
    [property: DataMember, MemoryPackOrder(3)] Range<long>? NextConversationRange)
{
    public static readonly ConversationRangeMeta None = new(
        ChatId.None,
        [],
        null,
        null);

    [IgnoreDataMember, MemoryPackIgnore]
    [field: AllowNull, MaybeNull]
    public ConversationId[] ConversationIds => field ??= ConversationRanges
        .Select(r => new ConversationId(ChatId, r.Start, AssumeValid.Option))
        .ToArray();
}
