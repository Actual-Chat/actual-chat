using ActualChat.Mesh;

namespace ActualChat.Rpc;

/// <summary>
/// In fact, it's a resolved ShardRef/NodeRef - with cached Node, IsLocal, IsOffline, etc.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ResolvedMeshRef
{
    public readonly MeshRpcPeerRefs Owner;
    public readonly ShardRef ShardRef;
    public readonly NodeRef NodeRef;
    public MeshRef MeshRef => ShardRef.IsNone ? NodeRef : ShardRef;
    public readonly MeshNode? Node;
    public bool IsLocal => ReferenceEquals(Node, Owner.ThisNode);
    public readonly MeshNodeState State;

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
        (Node, State) = meshState.GetNodeAndState(NodeRef);
    }

    public override string ToString()
    {
        var shardRefPrefix = ShardRef.IsNone ? "" : $"{ShardRef.Format()}->-";
        var nodeRef = NodeRef.Id.Value.NullIfEmpty() ?? "n/a";
        var isLocalSuffix = IsLocal ? "-local" : "";
        var stateSuffix = ShardRef.IsNone ? State.FormatSuffix() : "";
        return string.Concat("@", shardRefPrefix, nodeRef, isLocalSuffix, stateSuffix);
    }

    public Task WhenChanged(bool offlineIsOnline, CancellationToken cancellationToken)
    {
        var self = this;
        return Owner.MeshState.Computed
            .When(_ => !self.Equals(self.Latest, offlineIsOnline), cancellationToken);
    }

    // Equality

    public bool Equals(ResolvedMeshRef other, bool offlineIsOnline)
        => Owner == other.Owner
            && ShardRef == other.ShardRef
            && NodeRef == other.NodeRef
            && State.Normalize(offlineIsOnline) == other.State.Normalize(offlineIsOnline);
    public bool Equals(ResolvedMeshRef other)
        => Owner == other.Owner
            && ShardRef == other.ShardRef
            && NodeRef == other.NodeRef
            && State == other.State;
    public override int GetHashCode()
        => HashCode.Combine(Owner, ShardRef, NodeRef, State);
}
