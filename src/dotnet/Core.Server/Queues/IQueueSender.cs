namespace ActualChat.Queues;

/// <summary>
/// Enqueues commands for asynchronous processing.
/// </summary>
public interface IQueueSender
{
    Task Enqueue(QueueShardRef queueShardRef, QueuedCommand queuedCommand, CancellationToken cancellationToken = default);
}
