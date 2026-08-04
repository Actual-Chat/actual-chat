namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record UserMentionedInThreadChatEvent(
    [property: DataMember] ThreadChatId ThreadChatId,
    [property: DataMember] MentionRef[] MentionIds
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ThreadChatId;
}
