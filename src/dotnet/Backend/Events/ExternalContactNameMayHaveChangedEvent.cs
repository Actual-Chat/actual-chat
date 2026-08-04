namespace ActualChat;

[DataContract, MessagePackObject(true)]
public partial record ExternalContactNameMayHaveChangedEvent(
    [property: DataMember] UserId OwnerUserId,
    [property: DataMember] ImmutableArray<string> ExternalContactHashes
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => OwnerUserId;
}
