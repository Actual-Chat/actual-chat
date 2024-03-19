namespace ActualChat.Queues.Internal;

public interface IShardQueueSettings
{
    int MaxTryCount { get; }
}
