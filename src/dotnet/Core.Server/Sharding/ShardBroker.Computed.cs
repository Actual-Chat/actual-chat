using ActualLab.Fusion.Internal;
using ActualLab.Rpc;

namespace ActualChat.Sharding;

public sealed partial class ShardBroker
{
    public bool MustInvalidate<T>(T shardKey, CancellationToken cancellationToken)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>(new Requester(this));
        var shardIndex = shardKeyResolver.Invoke(shardKey);
        return MustInvalidate(shardIndex, cancellationToken);
    }

    public bool MustInvalidate(int shardIndex, CancellationToken cancellationToken)
    {
        shardIndex = shardIndex.PositiveModulo(ShardScheme.ShardCount);
        var invalidateUntil = ShardStates[shardIndex].InvalidateUntilState.Value;
        return invalidateUntil >= Clock.Now;
    }

    // [ComputeMethod] - alike
    public Task<bool> IsLeased<T>(T shardKey, CancellationToken cancellationToken)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>(new Requester(this));
        var shardIndex = shardKeyResolver.Invoke(shardKey);
        return IsLeased(shardIndex, cancellationToken);
    }

    // [ComputeMethod] - alike
    public Task<bool> IsLeased(int shardIndex, CancellationToken cancellationToken)
    {
        shardIndex = shardIndex.PositiveModulo(ShardScheme.ShardCount);
        var cCurrent = Computed.Current;
        var cBrokerState = State.Computed;
        var cLeaseState = cBrokerState.Value.ShardStates[shardIndex].LeaseState.Computed;
        if (cLeaseState.Value is null)
            return CompleteAsync();

        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cLeaseState);
        return ActualLab.Async.TaskExt.FalseTask;

        async Task<bool> CompleteAsync() {
            var waitTimeout = NewLeaseWaitTimeout.Next() - cBrokerState.Value.Age;
            if (waitTimeout <= TimeSpan.Zero)
                return false;

            try {
                cLeaseState = await cLeaseState
                    .When(x => x is not null, cancellationToken)
                    .WaitAsync(waitTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (cCurrent is not null)
                    ComputedImpl.AddDependency(cCurrent, cLeaseState);
                return true;
            }
            catch (TimeoutException) {
                return false;
            }
        }
    }

    // [ComputeMethod] - alike
    public Task<ShardLease> RequireLeaseOrReroute<T>(T shardKey, CancellationToken cancellationToken)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>(new Requester(this));
        var shardIndex = shardKeyResolver.Invoke(shardKey);
        return RequireLeaseOrReroute(shardIndex, cancellationToken);
    }

    // [ComputeMethod] - alike
    public Task<ShardLease> RequireLeaseOrReroute(int shardIndex, CancellationToken cancellationToken)
    {
        shardIndex = shardIndex.PositiveModulo(ShardScheme.ShardCount);
        var cCurrent = Computed.Current;
        var cBrokerState = State.Computed;
        var cLeaseState = cBrokerState.Value.ShardStates[shardIndex].LeaseState.Computed;
        if (cLeaseState.Value is null)
            return CompleteAsync();

        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cLeaseState);
        return (Task<ShardLease>)cLeaseState.GetValuePromise();

        async Task<ShardLease> CompleteAsync() {
            var waitTimeout = NewLeaseWaitTimeout.Next() - cBrokerState.Value.Age;
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
