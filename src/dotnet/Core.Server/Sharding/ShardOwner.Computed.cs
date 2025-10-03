using ActualLab.Fusion.Internal;
using ActualLab.Rpc;

namespace ActualChat.Sharding;

public sealed partial class ShardOwner
{
    public bool MustInvalidate<T>(T shardKey)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var invalidateUntil = State.Value.ShardStates[shardIndex].InvalidateUntilState.Value;
        return invalidateUntil >= Clock.Now;
    }

    // [ComputeMethod] - alike
    public Task<bool> IsOwned<T>(T shardKey, CancellationToken cancellationToken)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cState = State.Computed;
        var cCurrent = Computed.Current;
        var ownershipState = cState.Value.ShardStates[shardIndex].OwnershipState;
        var cOwnershipState = ownershipState.Computed;

        Task<bool> resultTask;
        if (cOwnershipState.Value is null) {
            var waitTimeout = OwnershipWaitTimeout.Next() - cState.Value.Age;
            if (waitTimeout > TimeSpan.Zero)
                return CompleteAsync(waitTimeout);

            resultTask = ActualLab.Async.TaskExt.FalseTask;
        }
        else
            resultTask = ActualLab.Async.TaskExt.TrueTask;

        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cOwnershipState);
        return resultTask;

        async Task<bool> CompleteAsync(TimeSpan waitTimeout) {
            try {
                await cOwnershipState
                    .When(x => x is not null, cancellationToken)
                    .WaitAsync(waitTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) {
                // Intended
            }

            cOwnershipState = ownershipState.Computed; // cOwnershipState.Update, but faster
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cOwnershipState);
            return cOwnershipState.Value is not null;
        }
    }

    // [ComputeMethod] - alike
    public Task<ShardOwnership> RequireOwnedOrReroute<T>(T shardKey, CancellationToken cancellationToken)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cState = State.Computed;
        var cCurrent = Computed.Current;
        var ownershipState = cState.Value.ShardStates[shardIndex].OwnershipState;
        var cOwnershipState = ownershipState.Computed;

        Task<ShardOwnership> resultTask;
        if (cOwnershipState.Value is null) {
            var waitTimeout = OwnershipWaitTimeout.Next() - cState.Value.Age;
            if (waitTimeout > TimeSpan.Zero)
                return CompleteAsync(waitTimeout);

            resultTask = Task.FromException<ShardOwnership>(RpcRerouteException.MustReroute());
        }
        else
            resultTask = (Task<ShardOwnership>)cOwnershipState.GetValuePromise();

        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cOwnershipState);
        return resultTask;

        async Task<ShardOwnership> CompleteAsync(TimeSpan waitTimeout) {
            try {
                await cOwnershipState
                    .When(x => x is not null, cancellationToken)
                    .WaitAsync(waitTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) {
                // Intended
            }

            cOwnershipState = ownershipState.Computed; // cOwnershipState.Update, but faster
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cOwnershipState);
            return cOwnershipState.Value ?? throw RpcRerouteException.MustReroute();
        }
    }
}
