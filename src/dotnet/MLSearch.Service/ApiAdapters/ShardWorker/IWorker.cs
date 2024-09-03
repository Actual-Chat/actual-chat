
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IWorker<in TCommand>
    where TCommand: notnull
{
    Task ExecuteAsync(TCommand job, CancellationToken cancellationToken);
}
