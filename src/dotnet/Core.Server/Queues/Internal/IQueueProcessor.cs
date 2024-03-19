namespace ActualChat.Queues.Internal;

public interface IQueueProcessor : IWorker
{
    IQueues Queues { get; }

    Task Enqueue(QueueShardRef queueShardRef, QueuedCommand queuedCommand, CancellationToken cancellationToken = default);
    Task WhenProcessing(TimeSpan maxCommandGap, CancellationToken cancellationToken = default);
}
