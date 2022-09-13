namespace ActualChat.Events;

public interface IEventConfiguration
{
    ICommand<Unit> JobCommand { get; }
    ShardKind ShardKind { get; }
    string ShardKey { get; }
    EventPriority Priority { get; }
}

[DataContract]
public record EventConfiguration(
    [property: DataMember] IEvent Event,
    [property: DataMember] ShardKind ShardKind = ShardKind.None,
    [property: DataMember] string ShardKey = "",
    [property: DataMember] EventPriority Priority = EventPriority.Normal) : IEventConfiguration
{
    [IgnoreDataMember]
    public ICommand<Unit> JobCommand => Event;
}

