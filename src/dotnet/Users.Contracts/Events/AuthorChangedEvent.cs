using ActualChat.Commands;

namespace ActualChat.Users.Events;

[DataContract]
public record AvatarChangedEvent(
    [property: DataMember] AvatarFull Avatar,
    [property: DataMember] AvatarFull? OldAvatar,
    [property: DataMember] ChangeKind ChangeKind
) : EventCommand;
