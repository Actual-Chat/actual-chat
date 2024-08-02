using ActualChat.Users;
using MemoryPack;

namespace ActualChat.Backend.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AvatarChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] AvatarFull Avatar,
    [property: DataMember, MemoryPackOrder(2)] AvatarFull? OldAvatar,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => Avatar.UserId;
}
