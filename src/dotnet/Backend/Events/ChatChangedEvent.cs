namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ChatChangedEvent(
    [property: DataMember] Chat.Chat Chat,
    [property: DataMember] Chat.Chat? OldChat,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Chat.Id.ShardKey;
}
