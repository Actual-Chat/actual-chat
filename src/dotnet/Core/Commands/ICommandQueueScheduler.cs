namespace ActualChat.Commands;

public interface ICommandQueueScheduler
{
    Task ProcessAlreadyQueued(TimeSpan timeout, CancellationToken cancellationToken);
}
