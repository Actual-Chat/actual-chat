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

    public static RpcPeerRef? Get(NodeRef nodeRef) => Get(nodeRef.Id);
    public static RpcPeerRef? Get(Symbol nodeId)
        => nodeId.IsEmpty ? null
            : _nodeRefCache.GetOrAdd(nodeId, n => RpcPeerRef.NewClient($"@node-{n.Value}", true));

    public static RpcPeerRef? Get(ShardRef shardRef)
        => shardRef.Scheme.BackendClientPeerRefs.GetOrDefault(shardRef.Index);

    public static bool IsNodeRef(this RpcPeerRef peerRef, out NodeRef nodeRef)
    {
        nodeRef = default;
        var key = peerRef.Key.Value;
        if (!key.OrdinalStartsWith("@node-"))
            return false;

        nodeRef = new NodeRef(key[6..], AssumeValid.Option);
        return true;
    }

    public static bool IsShardRef(this RpcPeerRef peerRef, out ShardRef shardRef)
    {
        shardRef = default;
        var key = peerRef.Key.Value;
        if (!key.OrdinalStartsWith("@shard-"))
            return false;

        var shardKey = key.AsSpan(7);
        var dashIndex = shardKey.IndexOf('-');
        if (dashIndex < 0)
            return false;

        if (!NumberExt.TryParseInt(shardKey[(dashIndex + 1)..], out var shardIndex))
            return false;

        var shardSchemeId = new string(shardKey[..dashIndex]);
        if (!ShardScheme.ById.TryGetValue(shardSchemeId, out var shardScheme))
            return false;

        shardRef = new ShardRef(shardScheme, shardIndex);
        return true;
    }
}
