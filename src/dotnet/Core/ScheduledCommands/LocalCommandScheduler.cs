namespace ActualChat.ScheduledCommands;

public class LocalCommandScheduler : WorkerBase
{
    private LocalCommandQueue LocalCommandQueue { get; }
    private ICommander Commander { get; }

    public LocalCommandScheduler(LocalCommandQueue localCommandQueue, ICommander commander)
    {
        LocalCommandQueue = localCommandQueue;
        Commander = commander;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var commands = LocalCommandQueue.ReadEvents(cancellationToken);
        await foreach (var command in commands.ConfigureAwait(false))
            _ = Commander.Start(command, true, cancellationToken);
    }
}
