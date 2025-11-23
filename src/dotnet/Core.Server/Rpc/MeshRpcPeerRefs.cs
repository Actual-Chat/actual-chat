using ActualLab.Rpc;

namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRefs
{
    private readonly ConcurrentDictionary<MeshRef, MeshRpcPeerRef> _peerRefs = new();
    private readonly Lock _lock = new ();

    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public MeshWatcher MeshWatcher { get; }
    public IState<MeshState> MeshState { get; }
    public MeshNode ThisNode { get; }
    public ShardOwners ShardOwners => field ??= MeshWatcher.Services.ShardOwners();

    public MeshRpcPeerRefs(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Services = services;
        MeshWatcher = services.MeshWatcher();
        MeshState = MeshWatcher.State;
        ThisNode = MeshWatcher.ThisNode;
    }

    public MeshRpcPeerRef Get(MeshRef meshRef)
    {
        if (meshRef.IsNone)
            throw new ArgumentOutOfRangeException(nameof(meshRef), "Can't route call to MeshRef.None.");

        // Normalizing meshRef
        var shardRef = meshRef.ShardRef;
        if (shardRef.IsNone) {
            if (meshRef.NodeRef == NodeRef.ThisNodeAlias)
                meshRef = ThisNode.Ref;
        }
        else
            meshRef = shardRef.Normalize();

        // Double-check locking
        // ReSharper disable once InconsistentlySynchronizedField
        if (_peerRefs.TryGetValue(meshRef, out var peerRef) && !peerRef.RouteState.IsChanged())
            return peerRef;
        lock (_lock) {
            if (_peerRefs.TryGetValue(meshRef, out peerRef) && !peerRef.RouteState.IsChanged())
                return peerRef;

            var version = (peerRef?.Version ?? 0) + 1;
            peerRef = NewMeshPeerRef(meshRef, version);
            _peerRefs[meshRef] = peerRef;
            return peerRef;
        }
    }

    // Private methods

    private MeshRpcPeerRef NewMeshPeerRef(MeshRef meshRef, int version)
    {
        var target = new ResolvedMeshRef(this, meshRef);
        var peerRef = new MeshRpcPeerRef(target, version);
        _ = MaybeRerouteEventually(peerRef, MeshWatcher.StopToken);
        return peerRef;
    }

    private async Task MaybeRerouteEventually(MeshRpcPeerRef peerRef, CancellationToken cancellationToken)
    {
        var target = peerRef.Target;
        if (target.ConnectionKind is RpcPeerConnectionKind.None)
            return;

        await target.WhenChanged(true, cancellationToken).ConfigureAwait(false);
        Log.LogWarning(
            "'{RpcPeerRef}': rerouting from {OldTarget} to {NewTarget}",
            peerRef, target, target.Latest);
        peerRef.MarkRerouted();
    }
}
