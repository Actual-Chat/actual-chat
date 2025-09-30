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
        var leaseState = cBrokerState.Value.ShardStates[shardIndex].LeaseState;
        var cLeaseState = leaseState.Computed;

        Task<bool> resultTask;
        if (cLeaseState.Value is null) {
            var waitTimeout = NewLeaseWaitTimeout.Next() - cBrokerState.Value.Age;
            if (waitTimeout > TimeSpan.Zero)
                return CompleteAsync(waitTimeout);

            resultTask = ActualLab.Async.TaskExt.FalseTask;
        }
        else
            resultTask = ActualLab.Async.TaskExt.TrueTask;

        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cLeaseState);
        return resultTask;

        async Task<bool> CompleteAsync(TimeSpan waitTimeout) {
            try {
                await cLeaseState
                    .When(x => x is not null, cancellationToken)
                    .WaitAsync(waitTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) {
                // Intended
            }

            cLeaseState = leaseState.Computed; // cLeaseState.Update, but faster
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cLeaseState);
            return cLeaseState.Value is not null;
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
        var leaseState = cBrokerState.Value.ShardStates[shardIndex].LeaseState;
        var cLeaseState = leaseState.Computed;

        Task<ShardLease> resultTask;
        if (cLeaseState.Value is null) {
            var waitTimeout = NewLeaseWaitTimeout.Next() - cBrokerState.Value.Age;
            if (waitTimeout > TimeSpan.Zero)
                return CompleteAsync(waitTimeout);

            resultTask = Task.FromException<ShardLease>(RpcRerouteException.MustReroute());
        }
        else
            resultTask = (Task<ShardLease>)cLeaseState.GetValuePromise();

        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cLeaseState);
        return resultTask;

        async Task<ShardLease> CompleteAsync(TimeSpan waitTimeout) {
            try {
                await cLeaseState
                    .When(x => x is not null, cancellationToken)
                    .WaitAsync(waitTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) {
                // Intended
            }

            cLeaseState = leaseState.Computed; // cLeaseState.Update, but faster
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cLeaseState);
            return cLeaseState.Value ?? throw RpcRerouteException.MustReroute();
        }
    }
}
