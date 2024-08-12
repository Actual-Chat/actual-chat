namespace ActualChat.Queues;

public interface IQueues : IWorker, IHasServices
{
    IReadOnlyDictionary<QueueRef, IQueueProcessor> Processors { get; }
    MomentClock Clock { get; }

    IQueueSender GetSender(QueueRef queueRef);
    Task Purge(CancellationToken cancellationToken = default);
}
