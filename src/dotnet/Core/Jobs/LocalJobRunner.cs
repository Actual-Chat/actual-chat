namespace ActualChat.Jobs;

public class LocalJobRunner : WorkerBase
{
    private LocalJobQueue LocalJobQueue { get; }
    private ICommander Commander { get; }

    public LocalJobRunner(LocalJobQueue localJobQueue, ICommander commander)
    {
        LocalJobQueue = localJobQueue;
        Commander = commander;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var jobs = LocalJobQueue.ReadJobs(cancellationToken);
        await foreach (var job in jobs.ConfigureAwait(false))
            _ = Commander.Start(job, cancellationToken);
    }
}
