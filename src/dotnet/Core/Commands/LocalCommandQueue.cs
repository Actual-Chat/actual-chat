namespace ActualChat.Commands;

public class LocalCommandQueue
{
    private Channel<ICommand> ScheduledCommands { get; }

    public LocalCommandQueue()
        => ScheduledCommands = Channel.CreateBounded<ICommand>(new BoundedChannelOptions(1000) {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public async Task Enqueue(ICommandConfiguration commandConfiguration, CancellationToken cancellationToken)
        => await ScheduledCommands.Writer.WriteAsync(commandConfiguration.Command, cancellationToken).ConfigureAwait(false);

    public IAsyncEnumerable<ICommand> Read(CancellationToken cancellationToken)
        => ScheduledCommands.Reader.ReadAllAsync(cancellationToken);
}
