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
        case ShardOwnershipStatus.OwnedByThisNode:
            return new(computed);
        case ShardOwnershipStatus.MappedToThisNode:
            return CompleteAsync();
        case ShardOwnershipStatus.MappedToOtherNode:
            throw RpcRerouteException.MustReroute("the shard isn't mapped to this node");
        default:
            throw StandardError.Internal($"Invalid ShardOwnershipStatus value: {shardState.OwnershipStatus}.");
        }

        async ValueTask<Computed<ShardOwner.ShardState>> CompleteAsync() {
            static bool HasOwnershipOrMustNotOwn(ShardOwner.ShardState x)
                => x.Ownership is not null || !x.MustOwn;

            computed = await shardStateState
                .WhenUnsafe(HasOwnershipOrMustNotOwn, cancellationToken)
                .ConfigureAwait(false);
            if (computed.Value.Ownership is not null)
                return computed;

            throw RpcRerouteException.MustReroute("the shard isn't mapped to this node");
        }
    }
}
