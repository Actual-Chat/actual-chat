namespace ActualChat.Commands;

#pragma warning disable CA1711

public interface ICommandQueue
{
    QueueId QueueId { get; }
    ICommandQueues Queues { get; }

    Task Enqueue(QueuedCommand command, CancellationToken cancellationToken = default);
}
