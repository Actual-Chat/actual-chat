namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record NewAccountEvent(
    [property: DataMember]
    UserId UserId
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}
