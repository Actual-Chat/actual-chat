using ActualChat.Hosting;

namespace ActualChat.Commands.Internal;

public class LocalCommandQueueIdProvider : ICommandQueueIdProvider
{
    public QueueId Get(QueuedCommand command)
        => new QueueId(HostRole.AnyServer, 0);
}
