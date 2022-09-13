namespace ActualChat.Events;

public class LocalEventQueue
{
    private Channel<ICommand> ScheduledCommands { get; }

    public LocalEventQueue()
        => ScheduledCommands = Channel.CreateBounded<ICommand>(new BoundedChannelOptions(1000) {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public async Task Enqueue(IEventConfiguration eventConfiguration, CancellationToken cancellationToken)
        => await ScheduledCommands.Writer.WriteAsync(eventConfiguration.JobCommand, cancellationToken).ConfigureAwait(false);

    public IAsyncEnumerable<ICommand> ReadEvents(CancellationToken cancellationToken)
        => ScheduledCommands.Reader.ReadAllAsync(cancellationToken);
}
