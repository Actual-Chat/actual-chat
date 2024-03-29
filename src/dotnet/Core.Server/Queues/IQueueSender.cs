namespace ActualChat.Queues;

public interface IQueueSender
{
    IQueues Queues { get; }

    Task Enqueue(QueueShardRef queueShardRef, QueuedCommand queuedCommand, CancellationToken cancellationToken = default);
}
