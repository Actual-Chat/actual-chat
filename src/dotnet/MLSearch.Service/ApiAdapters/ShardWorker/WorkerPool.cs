
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

// Note:
// This is super confusing. The scheme name MUST be a role name.
// It is getting a scheme from the role of the host. Super-super confusing.
// And the immediate next problem: Shard worker is expecting to accept a sharding scheme.
// This makes me think that I can create different schemes for different shard workers.
// However since it applies on the role level it's not a correct expectation.
// In order to solve this. I would suggest to:
// - Very Explicitly register a shard scheme against a role.
// - modify how this workers are getting registered:
//   services.AddWorker<HostRole, TWorker / TShardWorker>
// -

internal interface IWorkerPool<TJob, TJobId, TShardKey>
    where TJob : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    ValueTask PostAsync(TJob job, CancellationToken cancellationToken);
    ValueTask CancelAsync<TCancellation>(TCancellation jobCancellation, CancellationToken cancellationToken)
        where TCancellation : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>;
}

/// <summary>
/// Manages one pool of <see cref="TWorker"/> instances of <paramref name="shardConcurrencyLevel"/>
/// size per shard.
/// </summary>
internal class WorkerPool<TWorker, TJob, TJobId, TShardKey>(
    IServiceProvider services,
    DuplicateJobPolicy duplicateJobPolicy,
    int shardConcurrencyLevel,
    IShardIndexResolver<TShardKey> shardIndexResolver,
    IWorkerPoolShardFactory<TWorker, TJob, TJobId, TShardKey> workerPoolFactory
) : ActualChat.ShardWorker(services, ShardScheme.MLSearchBackend, typeof(TWorker).Name), IWorkerPool<TJob, TJobId, TShardKey>
    where TWorker : class, IWorker<TJob>
    where TJob : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    private readonly ConcurrentDictionary<int, IWorkerPoolShard<TWorker, TJob, TJobId, TShardKey>> _workerPoolShards = new();

    public async ValueTask PostAsync(TJob job, CancellationToken cancellationToken)
    {
        var poolShard = GetShard(job);
        await poolShard.PostAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CancelAsync<TCancellation>(TCancellation jobCancellation, CancellationToken cancellationToken)
        where TCancellation : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    {
        var poolShard = GetShard(jobCancellation);
        await poolShard.CancelAsync(jobCancellation.Id, cancellationToken).ConfigureAwait(false);
    }

    protected async override Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var poolShard = _workerPoolShards.AddOrUpdate(shardIndex,
            (key, arg) => arg.Factory.Create(key, arg.DuplicateJobPolicy, arg.ConcurrencyLevel),
            (key, _, arg) => arg.Factory.Create(key, arg.DuplicateJobPolicy, arg.ConcurrencyLevel),
            (Factory: workerPoolFactory, DuplicateJobPolicy: duplicateJobPolicy, ConcurrencyLevel: shardConcurrencyLevel));
        try {
            await poolShard.UseAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            // Clean up dictionary of worker pools if it still contains the pool being stopped
            _workerPoolShards.TryRemove(new KeyValuePair<int, IWorkerPoolShard<TWorker, TJob, TJobId, TShardKey>>(shardIndex, poolShard));
        }
    }

    private IWorkerPoolShard<TWorker, TJob, TJobId, TShardKey> GetShard<TItem>(TItem item)
        where TItem : IHasShardKey<TShardKey>
    {
        var shardIndex = shardIndexResolver.Resolve(item, ShardScheme);
        if (!_workerPoolShards.TryGetValue(shardIndex, out var poolShard)) {
            throw StandardError.NotFound<TWorker>(
                $"Shard #{shardIndex.Format()} of {nameof(TWorker)} pool is not found.");
        }

        return poolShard;
    }
}
