namespace ActualChat.Commands;

public interface ICommandQueueScheduler
{
    Task ProcessAlreadyQueued(TimeSpan maxIdleInterval, CancellationToken cancellationToken);
}
