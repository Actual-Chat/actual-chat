namespace ActualChat.Commands;

public interface ICommandQueue
{
    ICommandQueues Queues { get; }

    Task Enqueue(QueuedCommand command, CancellationToken cancellationToken = default);
}
