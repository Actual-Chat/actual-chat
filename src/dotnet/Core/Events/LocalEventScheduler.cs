namespace ActualChat.Events;

public class LocalEventScheduler : WorkerBase
{
    private LocalEventQueue LocalEventQueue { get; }
    private ICommander Commander { get; }

    public LocalEventScheduler(LocalEventQueue localEventQueue, ICommander commander)
    {
        LocalEventQueue = localEventQueue;
        Commander = commander;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var jobs = LocalEventQueue.ReadEvents(cancellationToken);
        await foreach (var job in jobs.ConfigureAwait(false))
            _ = Commander.Start(job, cancellationToken);
    }
}
