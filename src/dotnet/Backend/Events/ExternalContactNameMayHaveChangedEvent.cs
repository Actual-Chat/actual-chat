
namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record ExternalContactNameMayHaveChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] UserId OwnerUserId,
    [property: DataMember, MemoryPackOrder(2)] ImmutableArray<string> ExternalContactHashes
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => OwnerUserId;
}
