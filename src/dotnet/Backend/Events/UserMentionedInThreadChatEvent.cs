
namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record UserMentionedInThreadChatEvent(
    [property: DataMember, MemoryPackOrder(1)] ThreadChatId ThreadChatId,
    [property: DataMember, MemoryPackOrder(2)] MentionId[] MentionIds
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ThreadChatId;
}
