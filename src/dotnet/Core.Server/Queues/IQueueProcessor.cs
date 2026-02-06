namespace ActualChat.Queues;

/// <summary>
/// Processes commands from a message queue.
/// </summary>
public interface IQueueProcessor : IWorker, IQueueSender
{
    Task WhenProcessing(TimeSpan maxCommandGap, CancellationToken cancellationToken = default);
}
