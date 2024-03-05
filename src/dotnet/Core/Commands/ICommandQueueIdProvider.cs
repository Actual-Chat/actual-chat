namespace ActualChat.Commands;

public interface ICommandQueueIdProvider
{
    QueueId Get(QueuedCommand command);
}
