namespace ActualChat.Queues;

/// <summary>
/// Manages message queues for distributed command processing.
/// </summary>
public interface IQueues : IWorker, IHasServices
{
    IReadOnlyDictionary<QueueRef, IQueueProcessor> Processors { get; }
    MomentClock Clock { get; }

    IQueueSender GetSender(QueueRef queueRef);
    Task Purge(CancellationToken cancellationToken = default);
}
