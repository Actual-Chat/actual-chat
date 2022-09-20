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
        var jobs = LocalCommandQueue.ReadEvents(cancellationToken);
        await foreach (var job in jobs.ConfigureAwait(false))
            _ = Commander.Start(job, cancellationToken);
    }
}
