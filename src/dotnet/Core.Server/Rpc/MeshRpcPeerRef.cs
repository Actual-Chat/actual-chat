using ActualLab.Rpc;
namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRef : RpcPeerRef
{
    public readonly MeshRpcPeerRefs Owner;
    public readonly ResolvedMeshRef Resolved;
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
            var shardState = shardOwner.States[shardIndex].Value;
            if (shardState.MustOwn) {
                RouteState.LocalExecutionAwaiter = GetLocalExecutionAwaiter(RouteState, shardState);
                _ = MarkChangedWhenShardOwnershipEnds(shardState, RouteState.ChangedToken);
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

    private async Task MarkChangedWhenShardOwnershipEnds(
        ShardOwner.ShardState shardState,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        try {
            shardState = await shardState.When(x => x.Ownership is not null, cancellationToken).ConfigureAwait(false);
            await shardState.WhenNext(cancellationToken).ConfigureAwait(false);
        }
        catch {
            // Intended
        }
        finally {
            RouteState?.MarkChanged();
        }
    }

    private static Func<CancellationToken, ValueTask> GetLocalExecutionAwaiter(
        RpcRouteState routeState,
        ShardOwner.ShardState shardState)
        => async cancellationToken => {
            try {
                await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
            }
            catch (RpcRerouteException) {
                var shardOwners = shardState.ShardOwner.Host;
                if (shardOwners.StopToken.IsCancellationRequested)
                    throw new ObjectDisposedException(nameof(ShardOwners));
                if (shardOwners.Services.IsDisposedOrDisposing())
                    throw new ObjectDisposedException(nameof(IServiceProvider));

                routeState.MarkChanged();
                throw;
            }
        };
}
