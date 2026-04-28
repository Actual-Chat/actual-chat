namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ChatEntryChangedEvent(
    [property: DataMember] ChatEntry Entry,
    [property: DataMember] AuthorFull Author,
    [property: DataMember] ChangeKind ChangeKind,
    [property: DataMember] ChatEntry? OldEntry
) : EventCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => Entry.ChatId;
}
