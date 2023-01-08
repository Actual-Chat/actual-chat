using ActualChat.Commands;

namespace ActualChat.Users.Events;

[DataContract]
public record NewUserEvent(
    [property: DataMember] UserId UserId
) : EventCommand;
