using ActualChat.Commands;
using MemoryPack;

namespace ActualChat.Users.Events;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AvatarChangedEvent(
    [property: DataMember, MemoryPackOrder(1)] AvatarFull Avatar,
    [property: DataMember, MemoryPackOrder(2)] AvatarFull? OldAvatar,
    [property: DataMember, MemoryPackOrder(3)] ChangeKind ChangeKind
) : EventCommand;
