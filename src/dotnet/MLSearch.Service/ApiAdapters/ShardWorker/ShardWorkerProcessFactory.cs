
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IShardWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey>
    where TWorker : class, IWorker<TCommand>
    where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    IShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey> Create(int shardIndex, DuplicateJobPolicy duplicateJobPolicy);
}

internal class ShardWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey>(IServiceProvider services)
    : IShardWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey>
    where TWorker : class, IWorker<TCommand>
    where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    private readonly ObjectFactory<ShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey>> factoryMethod =
        ActivatorUtilities.CreateFactory<ShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey>>([typeof(int), typeof(DuplicateJobPolicy)]);

    public IShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey> Create(int shardIndex, DuplicateJobPolicy duplicateJobPolicy)
        => factoryMethod(services, [shardIndex, duplicateJobPolicy]);
}
