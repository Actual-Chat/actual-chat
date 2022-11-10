using ActualChat.Commands;

namespace ActualChat.Users.Events;

[DataContract]
public record NewUserEvent(
    [property: DataMember] Symbol UserId
    ) : IEvent;
