using ActualChat.Mesh;
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
    public bool IsLocal => ReferenceEquals(Node, Owner.ThisNode);
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
            var meshNode = shardMap[ShardRef.Key];
            NodeRef = meshNode?.Ref ?? default;
        }
        Node = meshState[NodeRef];
        ConnectionKind = IsLocal
            ? RpcPeerConnectionKind.Local
            : ShardRef.IsNone || Node?.State is not MeshNodeState.Dead
                ? RpcPeerConnectionKind.Remote
                : RpcPeerConnectionKind.None; // NodeRef pointing to a dead node
    }

    public override string ToString()
    {
        var shardRefPrefix = ShardRef.IsNone ? "" : $"{ShardRef.Format()}->-";
        var nodeRef = NodeRef.Id.Value.NullIfEmpty() ?? "n/a";
        var isLocalSuffix = IsLocal ? "-local" : "";
        var stateSuffix = ShardRef.IsNone
            ? (Node?.State ?? MeshNodeState.Unknown).FormatSuffix()
            : "";
        return string.Concat("@", shardRefPrefix, nodeRef, isLocalSuffix, stateSuffix);
    }

    public Task WhenChanged(bool collapseState, CancellationToken cancellationToken)
    {
        var self = this;
        return Owner.MeshState.Computed
            .When(_ => !self.IsChanged(collapseState), cancellationToken);
    }

    public bool IsChanged(bool collapseState)
        => IsChanged(Latest, collapseState);

    public bool IsChanged(ResolvedMeshRef other, bool collapseState)
        => Owner == other.Owner
            && ShardRef == other.ShardRef
            && NodeRef == other.NodeRef
            && (collapseState
                ? Node?.State.IsLive() == other.Node?.State.IsLive()
                : (Node?.State).OrUnknown() == (other.Node?.State).OrUnknown());
}
