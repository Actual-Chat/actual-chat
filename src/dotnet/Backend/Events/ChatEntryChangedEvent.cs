namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ChatEntryChangedEvent(
    [property: DataMember] ChatEntry Entry,
    [property: DataMember] AuthorFull Author,
    [property: DataMember] ChangeKind ChangeKind,
    [property: DataMember] ChatEntry? OldEntry
) : EventCommand, IHasShardKey
{
    [DataMember] public bool SuppressNotifications { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Entry.ChatId.ShardKey;
}
