namespace ActualChat.Commands.Internal;

public class LocalCommandQueueReader : ICommandQueueReader
{
    private LocalCommandQueue CommandQueue { get; }

    public LocalCommandQueueReader(LocalCommandQueue commandQueue)
        => CommandQueue = commandQueue;

    public IAsyncEnumerable<IQueuedCommand> Read(CancellationToken cancellationToken)
        => CommandQueue.Commands.Reader.ReadAllAsync(cancellationToken);

    public Task Ack(IQueuedCommand queuedCommand, CancellationToken cancellationToken)
    {
        CommandQueue.SetCompleted(queuedCommand);
        return Task.CompletedTask;
    }

    public Task NAck(IQueuedCommand queuedCommand, bool requeue, Exception? exception, CancellationToken cancellationToken)
    {
        if (!requeue)
            return Task.CompletedTask;

        CommandQueue.SetFailed(queuedCommand);
        return CommandQueue.Enqueue(queuedCommand, cancellationToken);
    }
}
