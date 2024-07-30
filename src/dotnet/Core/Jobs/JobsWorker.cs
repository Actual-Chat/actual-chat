namespace ActualChat.Jobs;

internal class JobsWorker(IJobsRunner jobsRunner) : WorkerBase
{
    protected override Task OnRun(CancellationToken cancellationToken)
        => jobsRunner.Start(cancellationToken);
}
