using ActualLab.Rpc;

namespace ActualChat.Rpc;

/// <summary>
/// In fact, it's a resolved ShardRef/NodeRef - with cached Node, IsLocal, IsOffline, etc.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ResolvedMeshRef
{
    public readonly MeshRpcPeerRefs Owner;
    public readonly ShardRef ShardRef;
    public readonly NodeRef NodeRef;
    public MeshRef MeshRef => ShardRef.IsNone ? NodeRef : ShardRef;
    public readonly MeshNode? Node;
    public readonly RpcPeerConnectionKind ConnectionKind;

    public ResolvedMeshRef Latest => new(Owner, MeshRef);

    public ResolvedMeshRef(MeshRpcPeerRefs owner, MeshRef meshRef)
    {
        Owner = owner;
        var meshState = owner.MeshState.LastNonErrorValue; // ComputedState.Value may throw OCE once MeshState is disposed
        if (meshRef.ShardRef.IsNone) {
            NodeRef = meshRef.NodeRef;
            if (NodeRef.IsNone)
                throw new ArgumentOutOfRangeException(nameof(meshRef));
        }
        else {
            ShardRef = meshRef.ShardRef;
            var shardMap = meshState.GetShardMap(ShardRef.Scheme);
            var shardIndex = ShardRef.GetShardIndex();
            var meshNode = shardMap[shardIndex];
            NodeRef = meshNode?.Ref ?? default;
        }
        Node = meshState[NodeRef];
        ConnectionKind = Node == Owner.ThisNode || Node?.Endpoint == Owner.ThisNode.Endpoint
            ? RpcPeerConnectionKind.Local
            : RpcPeerConnectionKind.Remote;
    }

    public override string ToString()
    {
        var shardRefPrefix = ShardRef.IsNone ? "" : $"{ShardRef.Format()}->-";
        var nodeRef = NodeRef.Id.Value.NullIfEmpty() ?? "n/a";
        var isLocalSuffix = ConnectionKind is RpcPeerConnectionKind.Local ? "-local" : "";
        var stateSuffix = ShardRef.IsNone
            ? Node?.State.FormatSuffix() ?? "-null-node"
            : "";
        return string.Concat("@", shardRefPrefix, nodeRef, isLocalSuffix, stateSuffix);
    }

    public Task WhenChanged(CancellationToken cancellationToken = default)
    {
        var self = this;
        return Owner.MeshState.Computed.When(_ => self.IsChanged(), cancellationToken);
    }

    public bool IsChanged()
        => IsChanged(Latest);

    public bool IsChanged(ResolvedMeshRef other)
        => Owner != other.Owner
            || ShardRef != other.ShardRef
            || NodeRef != other.NodeRef;
}
