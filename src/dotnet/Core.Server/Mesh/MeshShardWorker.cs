namespace ActualChat.Mesh;

public abstract class MeshShardWorker<TShardingDef>(IServiceProvider services)
    : MeshShardWorker(TShardingDef.Instance, services)
    where TShardingDef : MeshShardingDef, IMeshShardingDef<TShardingDef>;

public abstract class MeshShardWorker(MeshShardingDef shardingDef, IServiceProvider services) : WorkerBase
{
    protected MeshShardingDef ShardingDef { get; private init; } = shardingDef;
    protected MeshWatcher MeshWatcher { get; private init; } = services.MeshWatcher();

    protected override Task OnRun(CancellationToken cancellationToken)
        => Task.CompletedTask; // TBD
}
