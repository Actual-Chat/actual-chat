namespace ActualChat.Jobs;

public interface IJob
{
    Task Run(DateTimeOffset now, CancellationToken cancellationToken);
}
