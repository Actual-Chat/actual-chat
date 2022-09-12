namespace ActualChat.Jobs;

#pragma warning disable MA0049
public class Jobs
{
    private LocalJobQueue JobQueue { get; }

    public Jobs(LocalJobQueue jobQueue)
        => JobQueue = jobQueue;

    public async Task Schedule(
        JobConfiguration jobConfiguration,
        CancellationToken cancellationToken)
        => await JobQueue.Enqueue(jobConfiguration, cancellationToken).ConfigureAwait(false);
}
