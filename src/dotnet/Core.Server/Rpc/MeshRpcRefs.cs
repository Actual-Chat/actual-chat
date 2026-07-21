namespace ActualChat.Rpc;

public sealed class MeshRpcRefs
{
    private readonly ConcurrentDictionary<MeshRef, MeshRpcRef> _rpcRefs = new();

    internal ILogger Log { get; }

    public IServiceProvider Services { get; }
    public MeshWatcher MeshWatcher { get; }
    public IState<MeshState> MeshState { get; }
    public MeshNode ThisNode { get; }
    public ShardOwners ShardOwners => field ??= MeshWatcher.Services.ShardOwners();
    public IEnumerable<MeshRpcRef> RpcRefs => _rpcRefs.Values;

    public MeshRpcRefs(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Services = services;
        MeshWatcher = services.MeshWatcher();
        MeshState = MeshWatcher.State;
        ThisNode = MeshWatcher.ThisNode;
    }

    public MeshRpcRef Get(MeshRef meshRef)
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

        // Refs are stable: a topology change resets the ref's route, not the ref itself
        return _rpcRefs.GetOrAdd(meshRef, static (key, self) => new MeshRpcRef(self, key), this);
    }
}
