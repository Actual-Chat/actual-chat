using ActualChat.Events;

namespace ActualChat.Users.Events;

[DataContract]
public record NewUserEvent(
    [property: DataMember(Order = 0)]
    Symbol UserId) : IEvent
{
    [IgnoreDataMember]
    public ShardKind ShardKind => ShardKind.User;
    [IgnoreDataMember]
    public Symbol ShardKey => UserId;
}
