using ActualChat.Queues.Internal;

namespace ActualChat.Queues;

public interface IQueues : IWorker, IHasServices
{
    IMomentClock Clock { get; }

    IQueueProcessor GetProcessor(QueueRef queueRef);
    Task Purge(CancellationToken cancellationToken);
}
