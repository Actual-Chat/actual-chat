namespace ActualChat.Commands;

public class LocalCommandQueue : ICommandQueue
{
    private Channel<IBackendCommand> ScheduledCommands { get; }

    public bool HasCommands =>
        ScheduledCommands.Reader.Count > 0;

    public LocalCommandQueue()
        => ScheduledCommands = Channel.CreateBounded<IBackendCommand>(new BoundedChannelOptions(1000) {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public ValueTask Enqueue(IBackendCommand command, CancellationToken cancellationToken)
        => ScheduledCommands.Writer.WriteAsync(command, cancellationToken);

    public IAsyncEnumerable<IBackendCommand> Read(CancellationToken cancellationToken)
        => ScheduledCommands.Reader.ReadAllAsync(cancellationToken);
}
