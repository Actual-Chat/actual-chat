namespace ActualChat.Commands;

public interface ICommandQueueReader
{
    IAsyncEnumerable<IQueuedCommand> Read(CancellationToken cancellationToken);
    Task Ack(IQueuedCommand queuedCommand, CancellationToken cancellationToken);
    Task NAck(IQueuedCommand queuedCommand, bool requeue, Exception? exception, CancellationToken cancellationToken);
}
