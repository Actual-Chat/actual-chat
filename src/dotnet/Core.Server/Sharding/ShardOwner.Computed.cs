using ActualLab.Fusion.Internal;
using ActualLab.Rpc;

namespace ActualChat.Sharding;

public sealed partial class ShardOwner
{
    public bool MustInvalidate<T>(T shardKey)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var invalidateUntil = ShardStates[shardIndex].InvalidateUntilState.Value;
        return invalidateUntil >= Clock.Now;
    }

    // [ComputeMethod] - alike
    public Task<bool> IsLeased<T>(T shardKey, CancellationToken cancellationToken)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cCurrent = Computed.Current;
        var cOwnerState = State.Computed;
        var leaseState = cOwnerState.Value.ShardStates[shardIndex].LeaseState;
        var cLeaseState = leaseState.Computed;

        Task<bool> resultTask;
        if (cLeaseState.Value is null) {
            var waitTimeout = NewLeaseWaitTimeout.Next() - cOwnerState.Value.Age;
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
    public Task<ShardOwnership> RequireLeaseOrReroute<T>(T shardKey, CancellationToken cancellationToken)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cCurrent = Computed.Current;
        var cOwnerState = State.Computed;
        var leaseState = cOwnerState.Value.ShardStates[shardIndex].LeaseState;
        var cLeaseState = leaseState.Computed;

        Task<ShardOwnership> resultTask;
        if (cLeaseState.Value is null) {
            var waitTimeout = NewLeaseWaitTimeout.Next() - cOwnerState.Value.Age;
            if (waitTimeout > TimeSpan.Zero)
                return CompleteAsync(waitTimeout);

            resultTask = Task.FromException<ShardOwnership>(RpcRerouteException.MustReroute());
        }
        else
            resultTask = (Task<ShardOwnership>)cLeaseState.GetValuePromise();

        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cLeaseState);
        return resultTask;

        async Task<ShardOwnership> CompleteAsync(TimeSpan waitTimeout) {
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
