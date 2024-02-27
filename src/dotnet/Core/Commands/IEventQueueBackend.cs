namespace ActualChat.Commands;

public interface IEventQueueBackend: ICommandQueueBackend
{
    IAsyncEnumerable<QueuedCommand> Read(string consumerPrefix, Type commandType, CancellationToken cancellationToken);
}
