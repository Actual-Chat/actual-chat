namespace ActualChat.Commands;

public interface ICommandQueueBackend
{
    IAsyncEnumerable<QueuedCommand> Read(CancellationToken cancellationToken);
    ValueTask MarkCompleted(QueuedCommand command, CancellationToken cancellationToken);
    ValueTask MarkFailed(QueuedCommand command, bool mustRetry, Exception? exception, CancellationToken cancellationToken);
}
