namespace ActualChat.Jobs;

public class LocalJobQueue
{
    private Channel<ICommand> ScheduledCommands { get; }

    public LocalJobQueue()
        => ScheduledCommands = Channel.CreateBounded<ICommand>(new BoundedChannelOptions(1000) {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public async Task Enqueue(IJobConfiguration jobConfiguration, CancellationToken cancellationToken)
        => await ScheduledCommands.Writer.WriteAsync(jobConfiguration.JobCommand, cancellationToken).ConfigureAwait(false);

    public IAsyncEnumerable<ICommand> ReadJobs(CancellationToken cancellationToken)
        => ScheduledCommands.Reader.ReadAllAsync(cancellationToken);
}
