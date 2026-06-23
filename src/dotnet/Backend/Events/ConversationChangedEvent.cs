namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record ConversationChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] Chat.Conversation Conversation,
    [property: DataMember, MemoryPackOrder(2)] Chat.Conversation? OldConversation,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind,
    [property: DataMember, MemoryPackOrder(4)] bool SuppressNotification
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => Conversation.Id.ChatId;
}
