namespace ActualChat.Commands;

public interface ICommandQueue
{
    ValueTask Enqueue(IBackendCommand command, CancellationToken cancellationToken);

    IAsyncEnumerable<IBackendCommand> Read(CancellationToken cancellationToken);
}
