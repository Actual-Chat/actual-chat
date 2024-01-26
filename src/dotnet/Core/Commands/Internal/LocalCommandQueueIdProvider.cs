namespace ActualChat.Commands.Internal;

public class LocalCommandQueueIdProvider : ICommandQueueIdProvider
{
    public QueueId Get(QueuedCommand command)
        => default;
}
