namespace ActualChat.Jobs;

public interface IJobsRunner
{
    Task Start(CancellationToken token);
}
