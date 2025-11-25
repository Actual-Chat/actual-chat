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
    public ShardOwnershipState GetShardOwnershipState<T>(T shardKey, bool addDependency = true)
    {
        var cCurrent = addDependency ? Computed.Current : null;
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cState = State.Computed;
        var shardState = cState.Value.ShardStates[shardIndex];
        var ownershipState = shardState.OwnershipState;
        var cOwnershipState = ownershipState.Computed;
        if (cOwnershipState.Value is not null) {
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cOwnershipState);
            return ShardOwnershipState.Own;
        }

        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cState);
        return shardState.MustLock
            ? ShardOwnershipState.ToBeOwn
            : ShardOwnershipState.NotOwn;
    }

    // [ComputeMethod] - alike
    public Task<ShardOwnership> RequireOwnedOrReroute<T>(T shardKey, CancellationToken cancellationToken)
        => RequireOwnedOrReroute(shardKey, addDependency: true, cancellationToken);
    public Task<ShardOwnership> RequireOwnedOrReroute<T>(T shardKey, bool addDependency, CancellationToken cancellationToken)
    {
        var cCurrent = addDependency ? Computed.Current : null;
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cState = State.Computed;
        var shardState = cState.Value.ShardStates[shardIndex];
        var ownershipState = shardState.OwnershipState;
        var cOwnershipState = ownershipState.Computed;

        Task<ShardOwnership> resultTask;
        if (cOwnershipState.Value is not null) {
            // ShardOwnershipStatus.Own
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cOwnershipState);
            return (Task<ShardOwnership>)cOwnershipState.GetValuePromise();
        }

        if (shardState.MustLock) {
            // ShardOwnershipStatus.ToBeOwn
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cOwnershipState);
            return CompleteAsync();
        }

        // ShardOwnershipStatus.NotOwn
        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cState);
        throw RpcRerouteException.MustReroute();

        async Task<ShardOwnership> CompleteAsync() {
            var linkedCts = cancellationToken.LinkWith(shardState.CancelLockToken);
            var linkedToken = linkedCts.Token;
            try {
                cOwnershipState = await cOwnershipState
                    .When(x => x is not null, linkedToken)
                    .ConfigureAwait(false);
                return cOwnershipState.Value!;
            }
            catch (Exception e) when (e.IsCancellationOf(linkedToken) && !cancellationToken.IsCancellationRequested) {
                // If we're here, CancellationToken was canceled while the ownership was being acquired,
                // which means that at this point the shard isn't own already.
                throw RpcRerouteException.MustReroute();
            }
            finally {
                linkedCts.CancelAndDisposeSilently();
            }
        }
    }
}
