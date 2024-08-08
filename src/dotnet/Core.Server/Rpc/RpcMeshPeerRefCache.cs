using ActualChat.Mesh;

namespace ActualChat.Rpc;

public sealed class RpcMeshPeerRefCache
{
    private readonly ConcurrentDictionary<MeshRef, RpcMeshPeerRef> _peerRefs = new();
    private readonly object _lock = new ();
    private ILogger Log { get; }

    public MeshWatcher MeshWatcher { get; }
    public MeshNode OwnNode { get; }
    public RpcMeshPeerRef this[MeshRef meshRef] => Get(meshRef);

    public RpcMeshPeerRefCache(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        MeshWatcher = services.MeshWatcher();
        OwnNode = MeshWatcher.OwnNode;
    }

    public RpcMeshPeerRef Get(MeshRef meshRef)
    {
        if (meshRef.IsNone)
            throw new ArgumentOutOfRangeException(nameof(meshRef));

        var shardRef = meshRef.ShardRef;
        if (!shardRef.IsNone)
            meshRef = shardRef.Normalize();

        // Double-check locking
        // ReSharper disable once InconsistentlySynchronizedField
        if (_peerRefs.TryGetValue(meshRef, out var peerRef) && !peerRef.RerouteToken.IsCancellationRequested)
            return peerRef;
        lock (_lock) {
            if (_peerRefs.TryGetValue(meshRef, out peerRef) && !peerRef.RerouteToken.IsCancellationRequested)
                return peerRef;

            peerRef = Renew(meshRef, peerRef);
            _peerRefs[meshRef] = peerRef;
            return peerRef;
        }
    }

    // Private methods

    private RpcMeshPeerRef Renew(MeshRef meshRef, RpcMeshPeerRef? oldPeerRef)
    {
        var state = MeshWatcher.State.Value;
        var target = new MeshRefTarget(meshRef, state, OwnNode);
        var peerRef = new RpcMeshPeerRef(target, (oldPeerRef?.Version ?? 0) + 1);
        _ = MarkRerouted(peerRef, MeshWatcher.StopToken);
        return peerRef;
    }

    private async Task MarkRerouted(RpcMeshPeerRef peerRef, CancellationToken cancellationToken)
    {
        var target = peerRef.Target;
        var meshWatcherState = MeshWatcher.State;
        while (true) {
            await meshWatcherState.When(s => target.IsChanged(s), cancellationToken).ConfigureAwait(false);
            if (!target.ShardRef.IsNone)
                break; // MeshWatcher.NodeTimeout isn't applicable for shards, i.e. they change instantly

            await Task.Delay(MeshWatcher.NodeTimeout, cancellationToken).ConfigureAwait(false);
            if (target.IsChanged(meshWatcherState.Value))
                break; // The node is gone

            // The node is somehow back, so we'll rinse and repeat
        }
        Log.LogWarning("MarkRerouted: {RpcPeerRef}", peerRef);
        peerRef.MarkRerouted();
    }
}
