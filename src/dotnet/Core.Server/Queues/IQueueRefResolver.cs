namespace ActualChat.Queues;

public interface IQueueRefResolver
{
    QueueRef GetQueueRef(ICommand command);
    QueueShardRef GetQueueShardRef(ICommand command);
}
