namespace ActualChat.Commands;

#pragma warning disable MA0049
public class CommandGateway
{
    private LocalCommandQueue CommandQueue { get; }

    public CommandGateway(LocalCommandQueue commandQueue)
        => CommandQueue = commandQueue;

    public async Task Schedule(
        CommandConfiguration commandConfiguration,
        CancellationToken cancellationToken)
        => await CommandQueue.Enqueue(commandConfiguration, cancellationToken).ConfigureAwait(false);
}
