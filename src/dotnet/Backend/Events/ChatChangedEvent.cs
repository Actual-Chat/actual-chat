namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ChatChangedEvent(
    [property: DataMember] Chat.Chat Chat,
    [property: DataMember] Chat.Chat? OldChat,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Chat.Id;
}
