namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record ConversationRangeMeta(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] Range<long>[] ConversationLidRanges,
    [property: DataMember, Key(2)] Range<long>? PreviousConversationLidRange,
    [property: DataMember, Key(3)] Range<long>? NextConversationLidRange)
{
    [IgnoreDataMember, IgnoreMember]
    public ConversationId[] ConversationIds
        => field ??= ConversationLidRanges.Select(r => ConversationId.New(ChatId, r.Start)).ToArray();
}
