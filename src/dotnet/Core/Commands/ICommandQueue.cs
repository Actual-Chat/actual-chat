namespace ActualChat.Commands;

#pragma warning disable CA1711

public interface ICommandQueue
{
    ICommandQueues Queues { get; }

    Task Enqueue(QueuedCommand command, CancellationToken cancellationToken = default);
}
