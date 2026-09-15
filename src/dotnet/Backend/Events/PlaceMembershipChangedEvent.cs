namespace ActualChat;

[DataContract, MessagePackObject(true)]
public sealed partial record PlaceMembershipChangedEvent(
    [property: DataMember] UserId UserId,
    [property: DataMember] PlaceId PlaceId,
    [property: DataMember] bool HasLeft) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}
