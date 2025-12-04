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
            if (_shardState.MustOwn) {
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
        var cShardState = _shardState!;
        try {
            await cShardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        }
        catch (RpcRerouteException) {
            var shardOwners = cShardState.ShardOwner.Host;
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
        var asyncState = shardState.AsyncState;
        try {
            asyncState = await asyncState.When(x => x.Ownership is not null, cancellationToken).ConfigureAwait(false);
            await asyncState.WhenNext(cancellationToken).ConfigureAwait(false);
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
        await shardState.AsyncState.WhenNext().SilentAwait();
        RouteState?.MarkChanged();
    }
}
