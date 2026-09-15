namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ReadPositionChangedEvent(
    [property: DataMember] UserId UserId,
    [property: DataMember] ChatId ChatId,
    [property: DataMember] long EntryLid
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}
