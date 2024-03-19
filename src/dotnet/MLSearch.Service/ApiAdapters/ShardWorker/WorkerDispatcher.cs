
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IWorkerDispatcher<TCommand>
{
    ValueTask DispatchAsync(TCommand input, CancellationToken cancellationToken);
}

internal class WorkerDispatcher<TWorker, TCommand, TJobId, TShardKey>(
    IServiceProvider services,
    DuplicateJobPolicy duplicateJobPolicy,
    int singleShardConcurrencyLevel,
    IShardIndexResolver<TShardKey> shardIndexResolver,
    IWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey> workerFactory
) : ActualChat.ShardWorker(services, ShardScheme.MLSearchBackend, typeof(TWorker).Name), IWorkerDispatcher<TCommand>
    where TWorker : class, IWorker<TCommand>
    where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    private readonly ConcurrentDictionary<int, IWorkerProcess<TWorker, TCommand, TJobId, TShardKey>> _workerProcesses = new();

    public async ValueTask DispatchAsync(TCommand input, CancellationToken cancellationToken)
    {
        var shardIndex = shardIndexResolver.Resolve(input, ShardScheme);
        if (!_workerProcesses.TryGetValue(shardIndex, out var workerProcess)) {
            throw StandardError.NotFound<TWorker>(
                $"{nameof(TWorker)} instance for the shard #{shardIndex.Format()} is not found.");
        }
        await workerProcess.PostAsync(input, cancellationToken).ConfigureAwait(false);
    }
    protected async override Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var workerProcess = _workerProcesses.AddOrUpdate(shardIndex,
            (key, arg) => arg.Factory.Create(key, arg.DuplicateJobPolicy, arg.ConcurrencyLevel),
            (key, _, arg) => arg.Factory.Create(key, arg.DuplicateJobPolicy, arg.ConcurrencyLevel),
            (Factory: workerFactory, DuplicateJobPolicy: duplicateJobPolicy, ConcurrencyLevel: singleShardConcurrencyLevel));
        try {
            await workerProcess.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            // Clean up dictionary of workers if it still contains worker being stopped
            _workerProcesses.TryRemove(new KeyValuePair<int, IWorkerProcess<TWorker, TCommand, TJobId, TShardKey>>(shardIndex, workerProcess));
        }
    }
}
