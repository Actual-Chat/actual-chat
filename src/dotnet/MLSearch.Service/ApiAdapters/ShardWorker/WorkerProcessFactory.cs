
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey>
    where TWorker : class, IWorker<TCommand>
    where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    IWorkerProcess<TWorker, TCommand, TJobId, TShardKey> Create(int shardIndex, DuplicateJobPolicy duplicateJobPolicy);
}

internal class WorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey>(IServiceProvider services)
    : IWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey>
    where TWorker : class, IWorker<TCommand>
    where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    private readonly ObjectFactory<WorkerProcess<TWorker, TCommand, TJobId, TShardKey>> factoryMethod =
        ActivatorUtilities.CreateFactory<WorkerProcess<TWorker, TCommand, TJobId, TShardKey>>([typeof(int), typeof(DuplicateJobPolicy)]);

    public IWorkerProcess<TWorker, TCommand, TJobId, TShardKey> Create(int shardIndex, DuplicateJobPolicy duplicateJobPolicy)
        => factoryMethod(services, [shardIndex, duplicateJobPolicy]);
}
