using ActualChat.Commands;

namespace ActualChat.Users.Events;

[DataContract]
public record NewUserEvent(
    [property: DataMember(Order = 0)]
    string UserId) : IEvent;
