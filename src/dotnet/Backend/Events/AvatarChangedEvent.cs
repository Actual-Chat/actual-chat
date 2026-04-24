using ActualChat.Users;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial record AvatarChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] AvatarFull Avatar,
    [property: DataMember, MemoryPackOrder(2)] AvatarFull? OldAvatar,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => Avatar.UserId;
}
