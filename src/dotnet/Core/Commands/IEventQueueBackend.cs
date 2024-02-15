using ActualChat.Hosting;

namespace ActualChat.Commands;

public interface IEventQueueBackend: ICommandQueueBackend
{
    IAsyncEnumerable<QueuedCommand> Read(HostRole hostRole, CancellationToken cancellationToken);
}
