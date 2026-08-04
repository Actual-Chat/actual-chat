namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record NewAccountEvent(
    [property: DataMember]
    UserId UserId
) : EventCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
