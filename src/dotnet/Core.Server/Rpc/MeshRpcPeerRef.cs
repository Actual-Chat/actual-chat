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
        if (!shardRef.IsNone) {
            _routeChangedSource = new();
            var routeChangedToken = _routeChangedSource.Token;
            _shardState = Target.Owner.ShardOwners[shardRef.Scheme].GetShardState(shardRef.Key);
            _ = _shardState.WhenDisposed.ContinueWith(
                _ => MarkRerouted(),
                CancellationToken.None, TaskContinuationOptions.RunContinuationsAsynchronously, TaskScheduler.Default);
            RouteState = _shardState.MustLock
                ? new RpcShardRouteState(ShardLockAwaiter, routeChangedToken)
                : new RpcRouteState(routeChangedToken);
        }
        Initialize();
    }

    // Private methods

    private async ValueTask<CancellationToken> ShardLockAwaiter(CancellationToken cancellationToken)
    {
        var shardState = _shardState!;
        try {
            var shardOwnership = await shardState.RequireOwnership(cancellationToken).ConfigureAwait(false);
            return shardOwnership.LockToken;
        }
        catch (RpcRerouteException) {
            var shardOwners = shardState.ShardOwner.Host;
            if (shardOwners.StopToken.IsCancellationRequested)
                throw new ObjectDisposedException(nameof(ShardOwners));
            if (shardOwners.Services.IsDisposedOrDisposing())
                throw new ObjectDisposedException(nameof(IServiceProvider));

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
