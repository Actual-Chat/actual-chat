using ActualChat.Events;

namespace ActualChat.Users.Jobs;

[DataContract]
public record NewUserEvent(
    [property: DataMember(Order = 0)]
    string UserId) : IEvent;
