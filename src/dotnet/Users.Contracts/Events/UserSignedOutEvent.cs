using MemoryPack;

namespace ActualChat.Users.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record UserSignedOutEvent(
    [property: DataMember, MemoryPackOrder(1)] Symbol SessionId,
    [property: DataMember, MemoryPackOrder(2)] bool IsSignOutForced,
    [property: DataMember, MemoryPackOrder(3)] UserId UserId
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
