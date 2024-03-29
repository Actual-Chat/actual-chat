
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

// ReSharper disable once UnusedTypeParameter
internal interface IWorkerPoolShardFactory<TWorker, in TJob, in TJobId, in TShardKey>
    where TWorker : class, IWorker<TJob>
    where TJob : IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    IWorkerPoolShard<TJob, TJobId, TShardKey> Create(int shardIndex, DuplicateJobPolicy duplicateJobPolicy, int concurrencyLevel);
}

internal sealed class WorkerPoolShardFactory<TWorker, TJob, TJobId, TShardKey>(IServiceProvider services)
    : IWorkerPoolShardFactory<TWorker, TJob, TJobId, TShardKey>
    where TWorker : class, IWorker<TJob>
    where TJob : IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    private readonly ObjectFactory<WorkerPoolShard<TWorker, TJob, TJobId, TShardKey>> _factoryMethod =
        ActivatorUtilities.CreateFactory<WorkerPoolShard<TWorker, TJob, TJobId, TShardKey>>(
            [typeof(int), typeof(DuplicateJobPolicy), typeof(int)]
        );

    public IWorkerPoolShard<TJob, TJobId, TShardKey> Create(
        int shardIndex, DuplicateJobPolicy duplicateJobPolicy, int concurrencyLevel)
        => _factoryMethod(services, [shardIndex, duplicateJobPolicy, concurrencyLevel]);
}
