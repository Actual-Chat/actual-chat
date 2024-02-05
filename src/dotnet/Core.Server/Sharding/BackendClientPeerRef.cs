using ActualLab.Rpc;

namespace ActualChat;

public static class BackendClientPeerRef
{
    private static readonly ConcurrentDictionary<Symbol, RpcPeerRef> _nodeRefCache = new();

    public static RpcPeerRef? Get(MeshRef nodeRef)
    {
        if (!nodeRef.ShardRef.IsNone)
            return Get(nodeRef.ShardRef);
        if (!nodeRef.NodeRef.IsNone)
            return Get(nodeRef.NodeRef);
        return null;
    }

    public static RpcPeerRef? Get(MeshNodeId nodeRef) => Get(nodeRef.Id);
    public static RpcPeerRef? Get(Symbol nodeId)
        => nodeId.IsEmpty ? null
            : _nodeRefCache.GetOrAdd(nodeId, n => RpcPeerRef.NewClient($"@node-{n.Value}"));

    public static RpcPeerRef? Get(MeshShardRef shardRef) => Get(shardRef.ShardScheme, shardRef.ShardKey);
    public static RpcPeerRef? Get(ShardScheme shardScheme, int shardKey)
        => shardScheme.IsNone ? null
            : shardScheme.BackendClientPeerRefs[shardScheme.GetShardIndex(shardKey)];
}
