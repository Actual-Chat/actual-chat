using ActualChat.Mesh;

namespace ActualChat.Rpc;

public sealed class RpcMeshPeerRefCache
{
    private readonly ConcurrentDictionary<MeshRef, RpcMeshPeerRef> _peerRefs = new();
    private readonly ConcurrentDictionary<NodeRef, CpuTimestamp> _offlineNodeRefs = new();
    private readonly ConcurrentDictionary<NodeRef, Unit> _deadNodeRefs = new();
    private readonly object _lock = new ();
    private ILogger Log { get; }

    public MeshWatcher MeshWatcher { get; }
    public MeshNode OwnNode { get; }
    public RpcMeshPeerRef this[MeshRef meshRef] => Get(meshRef);

    public TimeSpan NodeOfflineToDeadTimeout { get; init; } = TimeSpan.FromMinutes(10);

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
        var target = new MeshRefTarget(meshRef, state, OwnNode, IsMarkedDead);
        var peerRef = new RpcMeshPeerRef(target, (oldPeerRef?.Version ?? 0) + 1);
        _ = MarkRerouted(peerRef, MeshWatcher.StopToken);
        return peerRef;
    }

    private async Task MarkRerouted(RpcMeshPeerRef peerRef, CancellationToken cancellationToken)
    {
        var target = peerRef.Target;
        var nodeRef = target.NodeRef;
        var meshWatcherState = MeshWatcher.State;
        while (true) {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var whenChanged = meshWatcherState.When(x => IsChanged(target, x), cts.Token);
            if (!nodeRef.IsNone) {
                if (target.State == MeshNodeState.Online) {
                    _offlineNodeRefs.Remove(nodeRef, out _);
                }
                else {
                    var offlineAt = _offlineNodeRefs.GetOrAdd(nodeRef, static _ => CpuTimestamp.Now);
                    if (target.State == MeshNodeState.Offline) {
                        var diesAt = offlineAt + NodeOfflineToDeadTimeout;
                        cts.CancelAfter((diesAt - CpuTimestamp.Now).Positive());
                    }
                    else // Already dead
                        cts.CancelAfter(0);
                }
            }

            await whenChanged.SuppressCancellationAwait(false);
            if (whenChanged.IsCanceled) {
                cancellationToken.ThrowIfCancellationRequested();
                // If we're here, NodeOfflineToDeadTimeout triggered cts cancellation
                // and no changes happened, so the nodeRef is dead.
                MarkDead(nodeRef);
                break;
            }

            // If we're here, mesh state is changed so that target is changed
            if (!target.ShardRef.IsNone)
                break; // ShardRef changes are always exposed instantly
            if (target.State != MeshNodeState.Online)
                break; // NodeRef changes are exposed instantly if the node was offline or dead

            // NodeRef target, which was online.
            // Let's wait a bit to see if the node becomes online again.
            await Task.Delay(MeshWatcher.NodeTimeout, cancellationToken).ConfigureAwait(false);
            if (IsChanged(target))
                break; // Nope, the node is still offline -> expose the change

            // The node is somehow back, so we'll repeat
        }
        Log.LogWarning("MarkRerouted: {RpcPeerRef}", peerRef);
        peerRef.MarkRerouted();
    }

    private bool IsChanged(MeshRefTarget target, MeshState? meshState = null)
    {
        meshState ??= MeshWatcher.State.Value;
        return target != new MeshRefTarget(target.MeshRef, meshState, OwnNode, IsMarkedDead);
    }

    private void MarkDead(NodeRef nodeRef)
    {
        if (!nodeRef.IsNone)
            _deadNodeRefs.TryAdd(nodeRef, default);
    }

    private bool IsMarkedDead(NodeRef nodeRef)
        => _deadNodeRefs.ContainsKey(nodeRef);
}
