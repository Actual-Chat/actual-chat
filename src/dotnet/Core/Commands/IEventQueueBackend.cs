namespace ActualChat.Commands;

public interface IEventQueueBackend
{
    IAsyncEnumerable<QueuedCommand> Read(string consumerPrefix, Type commandType, CancellationToken cancellationToken);
    ValueTask MarkCompleted(string consumerPrefix, QueuedCommand command, CancellationToken cancellationToken);
    ValueTask MarkFailed(string consumerPrefix, QueuedCommand command, Exception? exception, CancellationToken cancellationToken);
}
