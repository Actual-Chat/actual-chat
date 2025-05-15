using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record UserMentionedInThreadChatEvent(
    [property: DataMember, MemoryPackOrder(1)] ChatId ThreadChatId,
    [property: DataMember, MemoryPackOrder(2)] MentionId[] MentionIds
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ThreadChatId;
}
