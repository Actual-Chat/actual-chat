namespace ActualChat.Commands;

public interface ICommandQueue
{
    Task Enqueue(ICommand command, CancellationToken cancellationToken = default);
}
