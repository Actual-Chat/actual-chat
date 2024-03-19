using MemoryPack;

namespace ActualChat.Users.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AccountChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] AccountFull Account,
    [property: DataMember, MemoryPackOrder(2)] AccountFull? OldAccount,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => Account.Id;
}
