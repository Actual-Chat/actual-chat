using ActualLab.Rpc;
namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRef : RpcPeerRef
{
    private readonly CancellationTokenSource? _routeChangedSource;

    public readonly ResolvedMeshRef Target;
    public readonly ShardOwner.ShardState? ShardState;
    public readonly int Version;

    internal MeshRpcPeerRef(ResolvedMeshRef target, int version)
    {
        Target = target;
        Version = version;
        IsBackend = true;
        ConnectionKind = target.ConnectionKind;
        HostInfo = $"{target.ToString()}-v{version.Format()}";
        UseReferentialEquality = true;
        var shardRef = Target.ShardRef;
        if (shardRef.IsNone) {
            _routeChangedSource = null;
            RouteState = null;
        }
        else {
            _routeChangedSource = new();
            var routeChangedToken = _routeChangedSource.Token;
            ShardState = Target.Owner.ShardOwners[shardRef.Scheme].GetShardState(shardRef.Key);
            RouteState = ShardState.MustLock
                ? new RpcShardRouteState(ShardLockAwaiter, routeChangedToken)
                : new RpcRouteState(routeChangedToken);
        }
        Initialize();
        _ = ShardState?.WhenDisposed.ContinueWith(
            _ => _routeChangedSource.CancelAndDisposeSilently(),
            CancellationToken.None, TaskContinuationOptions.RunContinuationsAsynchronously, TaskScheduler.Default);
    }

    // Private and internal methods

    internal void MarkRerouted()
        => _routeChangedSource.CancelAndDisposeSilently();

    private async ValueTask<CancellationToken> ShardLockAwaiter(CancellationToken cancellationToken)
    {
        var shardOwnership = await ShardState!
            .RequireOwnership(addDependency: true, cancellationToken)
            .ConfigureAwait(false);
        return shardOwnership.LockToken;
    }
}
