namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ChatEntriesPurgedEvent(
    [property: DataMember] ChatId ChatId,
    [property: DataMember] long[] LocalIds
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}
