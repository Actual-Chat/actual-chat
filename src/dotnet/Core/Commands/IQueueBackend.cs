namespace ActualChat.Commands;

public interface IQueueBackend
{
    ValueTask Purge(CancellationToken cancellationToken);
}
