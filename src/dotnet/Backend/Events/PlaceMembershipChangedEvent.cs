using ActualChat.Contacts;

namespace ActualChat;

[DataContract, MessagePackObject(true)]
public sealed partial record PlaceMembershipChangedEvent(
    [property: DataMember] UserId UserId,
    [property: DataMember] PlaceId PlaceId,
    [property: DataMember] bool HasLeft) : EventCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
