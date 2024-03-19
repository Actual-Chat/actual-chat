
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IWorkerPoolShardFactory<TWorker, TCommand, TJobId, TShardKey>
    where TWorker : class, IWorker<TCommand>
    where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    IWorkerPoolShard<TWorker, TCommand, TJobId, TShardKey> Create(int shardIndex, DuplicateJobPolicy duplicateJobPolicy, int concurrencyLevel);
}

internal class WorkerPoolShardFactory<TWorker, TCommand, TJobId, TShardKey>(IServiceProvider services)
    : IWorkerPoolShardFactory<TWorker, TCommand, TJobId, TShardKey>
    where TWorker : class, IWorker<TCommand>
    where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    private readonly ObjectFactory<WorkerPoolShard<TWorker, TCommand, TJobId, TShardKey>> factoryMethod =
        ActivatorUtilities.CreateFactory<WorkerPoolShard<TWorker, TCommand, TJobId, TShardKey>>([typeof(int), typeof(DuplicateJobPolicy), typeof(int)]);

    public IWorkerPoolShard<TWorker, TCommand, TJobId, TShardKey> Create(
        int shardIndex, DuplicateJobPolicy duplicateJobPolicy, int concurrencyLevel)
        => factoryMethod(services, [shardIndex, duplicateJobPolicy, concurrencyLevel]);
}
