namespace ActualChat.Commands.Internal;

public class LocalCommandQueue : ICommandQueue
{
    public Channel<ICommand> Commands { get; }

    public LocalCommandQueue()
        => Commands = Channel.CreateBounded<ICommand>(
            new BoundedChannelOptions(1024) {
                FullMode = BoundedChannelFullMode.Wait,
            });

    public Task Enqueue(ICommand command, CancellationToken cancellationToken = default)
        => Commands.Writer.WriteAsync(command, cancellationToken).AsTask();
}
