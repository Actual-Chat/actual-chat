using ActualChat.Commands;
using MemoryPack;

namespace ActualChat.Users.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record NewUserEvent(
    [property: DataMember, MemoryPackOrder(1)]
    UserId UserId
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
