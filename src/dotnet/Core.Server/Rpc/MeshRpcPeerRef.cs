using ActualLab.Rpc;
namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRef : RpcPeerRef
{
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
            _shardState = Target.Owner.ShardOwners[shardRef.Scheme].GetShardState(shardRef.Key);
            RouteState = new();
            _ = RouteState.WhenChanged.ContinueWith(
                _ => Target.Owner.Log.LogWarning(
                    "'{RpcPeerRef}': rerouted from {OldTarget} to {NewTarget}",
                    this, Target, Target.Latest),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (_shardState.MustLock) {
                RouteState.LocalExecutionAwaiter = LocalExecutionAwaiter;
                _ = MarkChangedWhenShardOwnershipEnds(_shardState, RouteState.ChangedToken);
            }
            _ = MarkChangedWhenShardStateChanged(_shardState);
        }
        Initialize();
    }

    // Private methods

    private async ValueTask LocalExecutionAwaiter(CancellationToken cancellationToken)
    {
        var shardState = _shardState!;
        try {
            await shardState.RequireOwnership(cancellationToken).ConfigureAwait(false);
        }
        catch (RpcRerouteException) {
            var shardOwners = shardState.ShardOwner.Host;
            if (shardOwners.StopToken.IsCancellationRequested)
                throw new ObjectDisposedException(nameof(ShardOwners));
            if (shardOwners.Services.IsDisposedOrDisposing())
                throw new ObjectDisposedException(nameof(IServiceProvider));

            RouteState?.MarkChanged();
            throw;
        }
    }

    private async Task MarkChangedWhenShardOwnershipEnds(
        ShardOwner.ShardState shardState, CancellationToken cancellationToken)
    {
        try {
            var ownershipState = shardState.OwnershipState;
            // Waiting for ownership
            var computed = await ownershipState.Computed
                .When(x => x is not null, FixedDelayer.NoneUnsafe, cancellationToken)
                .ConfigureAwait(false);
            // It's always set to null after that
            await computed.WhenInvalidated(cancellationToken).SilentAwait(false);
        }
        catch {
            // Intended
        }
        finally {
            RouteState?.MarkChanged();
        }
    }

    private async Task MarkChangedWhenShardStateChanged(ShardOwner.ShardState shardState)
    {
        await shardState.WhenChanged.SilentAwait();
        RouteState?.MarkChanged();
    }
}
