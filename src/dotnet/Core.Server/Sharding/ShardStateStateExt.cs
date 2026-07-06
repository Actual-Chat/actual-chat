using ActualLab.Rpc;

namespace ActualChat.Sharding;

public static class ShardStateStateExt
{
    public static ValueTask<Computed<ShardOwner.ShardState>> RequireShardOwnership(
        this IState<ShardOwner.ShardState> shardStateState,
        CancellationToken cancellationToken = default)
    {
        var computed = shardStateState.Computed;
        var shardState = computed.Value;
        switch (shardState.OwnershipStatus) {
        case ShardOwnershipStatus.OwnedByThisNode when shardState.HasLiveOwnership:
            return new(computed);
        case ShardOwnershipStatus.OwnedByThisNode: // The lock is lost, wait for it to be re-acquired
        case ShardOwnershipStatus.MappedToThisNode:
            return CompleteAsync();
        case ShardOwnershipStatus.MappedToOtherNode:
            throw RpcRerouteException.MustReroute("the shard isn't mapped to this node");
        default:
            throw StandardError.Internal($"Invalid ShardOwnershipStatus value: {shardState.OwnershipStatus}.");
        }

        async ValueTask<Computed<ShardOwner.ShardState>> CompleteAsync() {
            static bool HasLiveOwnershipOrMustNotOwn(ShardOwner.ShardState x)
                => x.HasLiveOwnership || !x.MustOwn;

            computed = await shardStateState
                .WhenUnsafe(HasLiveOwnershipOrMustNotOwn, cancellationToken)
                .ConfigureAwait(false);
            if (computed.Value.Ownership is not null)
                return computed;

            throw RpcRerouteException.MustReroute("the shard isn't mapped to this node");
        }
    }
}
