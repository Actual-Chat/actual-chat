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
            if (ShardState.Value.MustOwn) {
                RouteState.LocalExecutionAwaiter = GetLocalExecutionAwaiter(RouteState);
                _ = MarkChangedWhenShardOwnershipEnds(RouteState.ChangedToken);
            }
            _ = MarkChangedWhenResolvedChanged();
        }
        Initialize();
    }

    // Private methods

    private async Task MarkChangedWhenResolvedChanged()
    {
        await Task.Yield();
        await Resolved.WhenChanged().ConfigureAwait(false);
        RouteState?.MarkChanged();
    }

    private async Task MarkChangedWhenShardOwnershipEnds(CancellationToken cancellationToken)
    {
        await Task.Yield();
        try {
            var cShardState = await ShardState!
                .WhenUnsafe(static x => x.Ownership is not null || !x.MustOwn, cancellationToken)
                .ConfigureAwait(false);
            if (cShardState.Value.Ownership is not null) // Got ownership -> await when it ends
                await cShardState.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
        catch {
            // Intended
        }
        finally {
            RouteState?.MarkChanged();
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
