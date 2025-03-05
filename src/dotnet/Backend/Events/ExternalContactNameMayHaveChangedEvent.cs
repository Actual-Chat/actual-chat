using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ExternalContactNameMayHaveChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] UserId OwnerUserId,
    [property: DataMember, MemoryPackOrder(2)] string ExternalContactLink
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => OwnerUserId;
}
