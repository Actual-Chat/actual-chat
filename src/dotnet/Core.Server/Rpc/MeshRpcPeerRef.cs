using ActualLab.Rpc;
namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRef : RpcPeerRef
{
    private CancellationTokenSource? _routeChangedSource;
    private readonly ShardOwner.ShardState? _shardState;

    public readonly ResolvedMeshRef Target;
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
            _shardState = Target.Owner.ShardOwners[shardRef.Scheme].GetShardState(shardRef.Key);
            RouteState = _shardState.MustLock
                ? new RpcShardRouteState(ShardLockAwaiter, routeChangedToken)
                : new RpcRouteState(routeChangedToken);
        }
        Initialize();
        _ = _shardState?.WhenDisposed.ContinueWith(
            _ => MarkRerouted(),
            CancellationToken.None, TaskContinuationOptions.RunContinuationsAsynchronously, TaskScheduler.Default);
    }

    // Private methods

    private async ValueTask<CancellationToken> ShardLockAwaiter(CancellationToken cancellationToken)
    {
        try {
            var shardOwnership = await _shardState!.RequireOwnership(cancellationToken).ConfigureAwait(false);
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
            "'{RpcPeerRef}': rerouted from {OldTarget} to {NewTarget}",
            this, Target, Target.Latest);
    }
}
