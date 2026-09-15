namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record AuthorsRemovedEvent(
    [property: DataMember] AuthorFull[] Authors
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Authors.Length > 0 ? Authors[0].ChatId.ShardKey : default;
}
