namespace ActualChat.Queues;

public interface IQueueSender
{
    Task Enqueue(QueueShardRef queueShardRef, QueuedCommand queuedCommand, CancellationToken cancellationToken = default);
}
