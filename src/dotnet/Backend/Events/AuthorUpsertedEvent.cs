namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record AuthorUpsertedEvent(
    [property: DataMember] AuthorFull Author,
    [property: DataMember] AuthorFull? OldAuthor
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Author.ChatId.ShardKey;
}
