namespace ActualChat.Queues;

public interface IQueueRefResolver
{
    QueueRef GetQueueRef(ICommand command, Requester requester);
    QueueShardRef GetQueueShardRef(ICommand command, Requester requester);
}
