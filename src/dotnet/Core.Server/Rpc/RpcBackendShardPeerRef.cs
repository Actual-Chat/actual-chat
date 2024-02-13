using ActualChat.Mesh;
using ActualLab.Rpc;

namespace ActualChat.Rpc;

public sealed record RpcBackendShardPeerRef : RpcPeerRef
{
    private readonly Task _updateTask;
    private readonly TaskCompletionSource<RpcBackendNodePeerRef?> _whenReadySource = new();
    private readonly TaskCompletionSource _whenObsoleteSource = new();
    private volatile RpcBackendShardPeerRef _latest;

    public MeshWatcher MeshWatcher { get; }
    public ShardRef ShardRef { get; }
    public int Index { get; }
    public Task<RpcBackendNodePeerRef?> WhenReady => _whenReadySource.Task;
    public Task WhenObsolete => _whenObsoleteSource.Task;

    public RpcBackendShardPeerRef Latest {
        get {
            var latest = _latest;
            if (!latest.WhenObsolete.IsCompleted)
                return latest;

            latest = latest.Latest;
            Interlocked.Exchange(ref _latest, latest);
            return latest;
        }
    }

    // ShardRef must be normalized here!
    public RpcBackendShardPeerRef(MeshWatcher meshWatcher, ShardRef shardRef, int index = 0)
        : base(GetKey(shardRef, index), false, true)
    {
        _latest = this;
        MeshWatcher = meshWatcher;
        ShardRef = shardRef;
        Index = index;
        _updateTask = Update();
    }

    public override string ToString()
        => $"backend @ {ShardRef} (#{GetHashCode()})";

    // This record relies on referential equality
    public bool Equals(RpcBackendShardPeerRef? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    // Private methods

    private static Symbol GetKey(ShardRef shardRef, int index)
        => $"@{shardRef}:{index.Format()}";

    private async Task Update()
    {
        try {
            var (shardScheme, shardIndex) = ShardRef;
            var meshState = MeshWatcher.State;
            var shardMap = meshState.Value.GetShardMap(shardScheme);
            var meshNode = shardMap[shardIndex];
            if (meshNode == null) {
                var c = await meshState
                    .When(x => x.GetShardMap(shardScheme)[shardIndex] != null)
                    .ConfigureAwait(false);
                meshNode = c.Value.GetShardMap(shardScheme)[shardIndex]!;
            }
            var nodeRef = meshNode.Ref;
            var peerRef = MeshWatcher.GetPeerRef(nodeRef).Require(nodeRef);
            _whenReadySource.TrySetResult(peerRef);
            await meshState
                .When(x => x.GetShardMap(shardScheme)[shardIndex] != meshNode)
                .ConfigureAwait(false);
            var latest = new RpcBackendShardPeerRef(MeshWatcher, ShardRef, Index + 1);
            Interlocked.Exchange(ref _latest, latest);
            _whenObsoleteSource.TrySetResult();
        }
        finally {
            _whenReadySource.TrySetResult(null);
        }
    }
}
