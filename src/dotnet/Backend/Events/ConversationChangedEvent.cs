namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ConversationChangedEvent(
    [property: DataMember] Chat.Conversation Conversation,
    [property: DataMember] Chat.Conversation? OldConversation,
    [property: DataMember] ChangeKind ChangeKind,
    [property: DataMember] bool SuppressNotification
) : EventCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Conversation.Id.ChatId;
}
