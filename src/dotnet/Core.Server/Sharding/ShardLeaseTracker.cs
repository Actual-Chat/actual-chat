using ActualLab.Fusion.Internal;
using ActualLab.Rpc;

namespace ActualChat.Sharding;

public sealed record ShardLeaseTracker(ShardBroker ShardBroker)
{
    private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(1);

    public TimeSpan WaitTimeout { get; init; } = DefaultWaitTimeout;

    // [ComputeMethod] - alike
    public Task<ShardLease> WhileLeased<T>(T shardKey, CancellationToken cancellationToken)
        => WhileLeased(shardKey, addDependency: true, cancellationToken);
    public Task<ShardLease> WhileLeased<T>(T shardKey, bool addDependency, CancellationToken cancellationToken)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>(new Requester(this));
        var shardIndex = shardKeyResolver.Invoke(shardKey);
        return WhileLeasedByShardIndex(shardIndex, addDependency, cancellationToken);
    }

    // [ComputeMethod] - alike
    public Task<ShardLease> WhileLeasedByShardIndex(int shardIndex, CancellationToken cancellationToken)
        => WhileLeasedByShardIndex(shardIndex, addDependency: true, cancellationToken);
    public Task<ShardLease> WhileLeasedByShardIndex(int shardIndex, bool addDependency, CancellationToken cancellationToken)
    {
        shardIndex = shardIndex.PositiveModulo(ShardBroker.ShardScheme.ShardCount);
        var cCurrent = addDependency ? Computed.GetCurrent() : null;
        var cBrokerState = ShardBroker.State.Computed;
        var cLeaseState = cBrokerState.Value.ShardStates[shardIndex].LeaseState.Computed;
        if (cLeaseState.Value is null)
            return CompleteAsync();

        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cLeaseState);
        return (Task<ShardLease>)cLeaseState.GetValuePromise();

        async Task<ShardLease> CompleteAsync() {
            var waitTimeout = WaitTimeout - cBrokerState.Value.Age;
            if (waitTimeout <= TimeSpan.Zero)
                throw RpcRerouteException.MustReroute();

            try {
                cLeaseState = await cLeaseState
                    .When(x => x is not null, cancellationToken)
                    .WaitAsync(waitTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (cCurrent is not null)
                    ComputedImpl.AddDependency(cCurrent, cLeaseState);
                return cLeaseState.Value!;
            }
            catch (TimeoutException) {
                throw RpcRerouteException.MustReroute();
            }
        }
    }
}
