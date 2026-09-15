namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record UserSignedInEvent(
    [property: DataMember] UserId UserId,
    [property: DataMember] Session Session
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}
