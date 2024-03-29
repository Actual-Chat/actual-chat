namespace ActualChat.Queues;

public interface IQueueProcessor : IWorker, IQueueSender
{
    Task WhenProcessing(TimeSpan maxCommandGap, CancellationToken cancellationToken = default);
}
