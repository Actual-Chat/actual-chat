namespace ActualChat.Events;

public interface IEvent
{
    ShardKind ShardKind { get; }
    Symbol ShardKey { get; }
}
