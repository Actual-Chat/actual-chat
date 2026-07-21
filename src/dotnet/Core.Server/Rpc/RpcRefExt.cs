using ActualLab.Rpc;

namespace ActualChat.Rpc;

public static class RpcRefExt
{
    public static TRef Require<TRef>(this TRef? rpcRef)
        where TRef : RpcRef
        => rpcRef ?? throw new ArgumentNullException(nameof(rpcRef));

    public static TRef Require<TRef>(this TRef? rpcRef, MeshRef meshRef)
        where TRef : RpcRef
        => rpcRef ?? throw new ArgumentNullException(nameof(rpcRef), $"Invalid MeshRef: {meshRef}");
    public static TRef Require<TRef>(this TRef? rpcRef, NodeRef nodeRef)
        where TRef : RpcRef
        => rpcRef ?? throw new ArgumentNullException(nameof(rpcRef), $"Invalid NodeRef: {nodeRef}");
    public static TRef Require<TRef>(this TRef? rpcRef, ShardRef shardRef)
        where TRef : RpcRef
        => rpcRef ?? throw new ArgumentNullException(nameof(rpcRef), $"Invalid ShardRef: {shardRef}");
}
