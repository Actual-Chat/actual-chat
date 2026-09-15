namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ExternalContactNameMayHaveChangedEvent(
    [property: DataMember] UserId OwnerUserId,
    [property: DataMember] ImmutableArray<string> ExternalContactHashes
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => OwnerUserId.ShardKey;
}
