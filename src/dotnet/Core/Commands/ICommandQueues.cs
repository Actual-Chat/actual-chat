namespace ActualChat.Commands;

public interface ICommandQueues : IHasServices
{
    IMomentClock Clock { get; }
    ICommandQueue this[QueueId queueId] { get; }

    ICommandQueueBackend GetBackend(QueueId queueId);

    Task Purge(CancellationToken cancellationToken);
}
