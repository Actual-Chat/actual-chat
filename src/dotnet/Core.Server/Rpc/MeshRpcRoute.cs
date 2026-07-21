using ActualLab.Fusion.Internal;
using ActualLab.Rpc;

namespace ActualChat.Rpc;

/// <summary>
/// One route generation of a shard-based <see cref="MeshRpcRef"/>: carries the
/// <see cref="ResolvedMeshRef"/> for this generation and marks itself as changed
/// when the shard map or local shard ownership changes.
/// </summary>
public sealed class MeshRpcRoute : RpcRoute
{
    public readonly ResolvedMeshRef Resolved;

    // ReSharper disable once MemberCanBePrivate.Global
    public new MeshRpcRef Ref => (MeshRpcRef)base.Ref;

    internal MeshRpcRoute(MeshRpcRef rpcRef) : base(rpcRef)
    {
        Resolved = new ResolvedMeshRef(rpcRef.Owner, rpcRef.MeshRef);
        ConnectionKind = Resolved.ConnectionKind;
        _ = WhenChanged.ContinueWith(
            _ => rpcRef.Owner.Log.LogWarning(
                "'{Route}': rerouted from {OldTarget} to {NewTarget}",
                this, Resolved, Resolved.Latest),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // Gating this on a MustOwn snapshot is racy: ShardOwner may lag behind the mesh map,
        // and local calls made via an awaiter-less route get no shard ownership dependency,
        // so their computeds are never invalidated once the shard migrates away.
        // ConnectionKind (vs Node == ThisNode) also covers the endpoint-equality branch:
        // whenever calls execute locally, they must gate on local shard ownership.
        if (Resolved.ConnectionKind == RpcPeerConnectionKind.Local) {
            LocalExecutionAwaiter = CreateLocalExecutionAwaiter();
            _ = MarkChangedWhenShardOwnershipEnds();
        }
        _ = MarkChangedWhenResolvedChanged();
    }

    // Protected methods

    protected override string GetTargetString()
        => Resolved.ToString();

    // Private methods

    private async Task MarkChangedWhenResolvedChanged()
    {
        var cancellationToken = ChangedToken;
        try {
            await Resolved.WhenChanged(cancellationToken).ConfigureAwait(false);
        }
        finally {
            if (!cancellationToken.IsCancellationRequested)
                MarkChanged();
        }
    }

    private async Task MarkChangedWhenShardOwnershipEnds()
    {
        var cancellationToken = ChangedToken;
        try {
            // No "|| !x.MustOwn" here: this route may be created before ShardOwner catches up
            // with the mesh map; "the shard is mapped elsewhere" is MarkChangedWhenResolvedChanged's job
            var cShardState = await Ref.ShardState!
                .WhenUnsafe(static x => x.Ownership is not null, cancellationToken)
                .ConfigureAwait(false);
            await cShardState.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
        finally {
            if (!cancellationToken.IsCancellationRequested)
                MarkChanged();
        }
    }

    private Func<bool, CancellationToken, ValueTask> CreateLocalExecutionAwaiter()
        => async (addDependency, cancellationToken) => {
            try {
                var cShardStateWhenOwns = await Ref.ShardState!
                    .RequireShardOwnership(cancellationToken)
                    .ConfigureAwait(false);
                if (addDependency && Computed.Current is { } cCurrent)
                    ComputedImpl.AddDependency(cCurrent, cShardStateWhenOwns);
            }
            catch (RpcRerouteException) {
                MarkChanged();
                throw;
            }
        };
}
