using ActualLab.Rpc;
namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRef : RpcPeerRef
{
    private CancellationTokenSource? _routeChangedSource;

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
            _ => MarkRerouted(),
            CancellationToken.None, TaskContinuationOptions.RunContinuationsAsynchronously, TaskScheduler.Default);
    }

    // Private methods

    private async ValueTask<CancellationToken> ShardLockAwaiter(CancellationToken cancellationToken)
    {
        try {
            var shardOwnership = await ShardState!.RequireOwnership(cancellationToken).ConfigureAwait(false);
            return shardOwnership.LockToken;
        }
        catch (RpcRerouteException) {
            MarkRerouted();
            throw;
        }
    }

    private void MarkRerouted()
    {
        var routeChangedSource = Interlocked.Exchange(ref _routeChangedSource, null);
        if (routeChangedSource is null)
            return;

        _routeChangedSource.CancelAndDisposeSilently();
        Target.Owner.Log.LogWarning(
            "'{RpcPeerRef}': rerouting from {OldTarget} to {NewTarget}",
            this, Target, Target.Latest);
    }
}
