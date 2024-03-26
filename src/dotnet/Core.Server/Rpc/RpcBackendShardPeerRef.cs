using ActualChat.Mesh;
using ActualLab.Rpc;

namespace ActualChat.Rpc;

public sealed record RpcBackendShardPeerRef : RpcPeerRef
{
    private readonly TaskCompletionSource<RpcBackendNodePeerRef?> _whenReadySource = new();
    private readonly TaskCompletionSource _whenObsoleteSource = new();
    private volatile RpcBackendShardPeerRef _latest;

    public MeshWatcher MeshWatcher { get; }
    public ShardRef ShardRef { get; }
    public int Version { get; }
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
    public RpcBackendShardPeerRef(MeshWatcher meshWatcher, ShardRef shardRef, int version = 0)
        : base(GetKey(shardRef, version), false, true)
    {
        _latest = this;
        MeshWatcher = meshWatcher;
        ShardRef = shardRef.RequireValid();
        Version = version;
        _ = Update();
    }

    public override string ToString()
        => Key;

    // This record relies on referential equality
    public bool Equals(RpcBackendShardPeerRef? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    // Private methods

    private static Symbol GetKey(ShardRef shardRef, int version)
        => $"@{shardRef.Format()}-v{version.Format()}";

    private async Task Update()
    {
        try {
            var shardScheme = ShardRef.Scheme;
            var shardIndex = ShardRef.GetShardIndex();
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
            var latest = new RpcBackendShardPeerRef(MeshWatcher, ShardRef, Version + 1);
            Interlocked.Exchange(ref _latest, latest);
            _whenObsoleteSource.TrySetResult();
        }
        finally {
            _whenReadySource.TrySetResult(null);
        }
    }
}
