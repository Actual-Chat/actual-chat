namespace ActualChat.Commands;

public interface ICommandQueueReader
{
    IAsyncEnumerable<QueuedCommand> Read(CancellationToken cancellationToken);
    ValueTask MarkCompleted(QueuedCommand command, CancellationToken cancellationToken);
    ValueTask MarkFailed(QueuedCommand command, bool mustRetry, Exception? exception, CancellationToken cancellationToken);
}
