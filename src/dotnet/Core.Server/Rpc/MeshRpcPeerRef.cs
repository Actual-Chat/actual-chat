using ActualLab.Fusion.Internal;
using ActualLab.Rpc;
namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRef : RpcPeerRef
{
    public readonly MeshRpcPeerRefs Owner;
    public readonly ResolvedMeshRef Resolved;
    public readonly IState<ShardOwner.ShardState>? ShardState;
    public readonly int Version;

    // Computed properties
    public ShardRef ShardRef => Resolved.ShardRef;
    public NodeRef NodeRef => Resolved.NodeRef;
    public MeshRef MeshRef => Resolved.MeshRef;
    public MeshNode? Node => Resolved.Node;

    internal MeshRpcPeerRef(MeshRpcPeerRefs owner, MeshRef meshRef, int version)
    {
        Owner = owner;
        Resolved = new ResolvedMeshRef(owner, meshRef);
        Version = version;
        IsBackend = true;
        ConnectionKind = Resolved.ConnectionKind;
        HostInfo = $"{Resolved.ToString()}-v{version.Format()}";
        UseReferentialEquality = true;

        if (!ShardRef.IsNone) {
            // Any MeshRpcPeerRef with ShardRef has RouteState;
            // any MeshRpcPeerRef without ShardRef (i.e., with only a NodeRef) doesn't.
            RouteState = new RpcRouteState();
            _ = RouteState.WhenChanged.ContinueWith(
                _ => Owner.Log.LogWarning(
                    "'{RpcPeerRef}': rerouted from {OldTarget} to {NewTarget}",
                    this, Resolved, Resolved.Latest),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            var shardIndex = ShardRef.GetShardIndex();
            var shardOwner = Resolved.Owner.ShardOwners[ShardRef.Scheme];
            ShardState = shardOwner.States[shardIndex];

            // Gating this on a MustOwn snapshot is racy: ShardOwner may lag behind the mesh map,
            // and local calls made via an awaiter-less ref get no shard ownership dependency,
            // so their computeds are never invalidated once the shard migrates away.
            var isLocal = Resolved.Node == Owner.ThisNode;
            if (isLocal) {
                RouteState.LocalExecutionAwaiter = GetLocalExecutionAwaiter(RouteState);
                _ = MarkChangedWhenShardOwnershipEnds();
            }
            _ = MarkChangedWhenResolvedChanged();
        }
        Initialize();
    }

    // Private methods

    private async Task MarkChangedWhenResolvedChanged()
    {
        var routeState = RouteState!;
        var cancellationToken = routeState.ChangedToken;
        try {
            await Resolved.WhenChanged(cancellationToken).ConfigureAwait(false);
        }
        finally {
            if (!cancellationToken.IsCancellationRequested)
                routeState.MarkChanged();
        }
    }

    private async Task MarkChangedWhenShardOwnershipEnds()
    {
        var routeState = RouteState!;
        var cancellationToken = routeState.ChangedToken;
        try {
            // No "|| !x.MustOwn" here: this ref may be created before ShardOwner catches up
            // with the mesh map; "the shard is mapped elsewhere" is MarkChangedWhenResolvedChanged's job
            var cShardState = await ShardState!
                .WhenUnsafe(static x => x.Ownership is not null, cancellationToken)
                .ConfigureAwait(false);
            await cShardState.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
        finally {
            if (!cancellationToken.IsCancellationRequested)
                routeState.MarkChanged();
        }
    }

    private Func<bool, CancellationToken, ValueTask> GetLocalExecutionAwaiter(RpcRouteState routeState)
        => async (addDependency, cancellationToken) => {
            try {
                var cShardStateWhenOwns = await ShardState!
                    .RequireShardOwnership(cancellationToken)
                    .ConfigureAwait(false);
                if (addDependency && Computed.Current is { } cCurrent)
                    ComputedImpl.AddDependency(cCurrent, cShardStateWhenOwns);
            }
            catch (RpcRerouteException) {
                routeState.MarkChanged();
                throw;
            }
        };
}
