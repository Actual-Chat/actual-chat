
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IWorker<TCommand>
    where TCommand: notnull
{
    Task ExecuteAsync(TCommand input, CancellationToken cancellationToken);
}
