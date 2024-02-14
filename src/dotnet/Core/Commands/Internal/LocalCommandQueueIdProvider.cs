using ActualChat.Hosting;

namespace ActualChat.Commands.Internal;

public class LocalCommandQueueIdProvider : ICommandQueueIdProvider
{
    public QueueId Get(QueuedCommand command)
        => new QueueId(HostRole.DefaultQueue, 0);
}
