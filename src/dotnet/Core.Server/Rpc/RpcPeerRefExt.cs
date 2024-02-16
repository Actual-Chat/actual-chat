using ActualLab.Rpc;

namespace ActualChat.Rpc;

public static class RpcPeerRefExt
{
    public static TPeerRef Require<TPeerRef>(this TPeerRef? peerRef)
        where TPeerRef : RpcPeerRef
        => peerRef ?? throw new ArgumentNullException(nameof(peerRef));

    public static TPeerRef Require<TPeerRef>(this TPeerRef? peerRef, MeshRef meshRef)
        where TPeerRef : RpcPeerRef
        => peerRef ?? throw new ArgumentNullException(nameof(peerRef), $"Invalid MeshRef: {meshRef}");
    public static TPeerRef Require<TPeerRef>(this TPeerRef? peerRef, NodeRef nodeRef)
        where TPeerRef : RpcPeerRef
        => peerRef ?? throw new ArgumentNullException(nameof(peerRef), $"Invalid NodeRef: {nodeRef}");
    public static TPeerRef Require<TPeerRef>(this TPeerRef? peerRef, ShardRef shardRef)
        where TPeerRef : RpcPeerRef
        => peerRef ?? throw new ArgumentNullException(nameof(peerRef), $"Invalid ShardRef: {shardRef}");
}
