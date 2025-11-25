using ActualLab.Rpc;

namespace ActualChat.Rpc;

public sealed class MeshRpcPeerRefs
{
    private readonly ConcurrentDictionary<MeshRef, MeshRpcPeerRef> _peerRefs = new();
    private readonly Lock _lock = new ();

    internal ILogger Log { get; }

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
            var target = new ResolvedMeshRef(this, meshRef);
            peerRef = new MeshRpcPeerRef(target, version);
            _peerRefs[meshRef] = peerRef;
            return peerRef;
        }
    }
}
