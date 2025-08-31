namespace ActualChat;

public abstract class ShardWorker(IServiceProvider services, ShardScheme shardScheme, string? keyPrefix = null)
    : ShardLocker(services, shardScheme, keyPrefix)
{
    protected override Task UseShard(ShardLock shardLock, CancellationToken cancellationToken)
        => OnRun(shardLock.State.Index, cancellationToken);

    protected abstract Task OnRun(int shardIndex, CancellationToken cancellationToken);
}
