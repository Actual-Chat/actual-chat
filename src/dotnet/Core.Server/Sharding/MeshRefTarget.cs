using ActualChat.Mesh;
using Cysharp.Text;

namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
public readonly record struct MeshRefTarget
{
    public readonly ShardRef ShardRef;
    public readonly NodeRef NodeRef;
    public MeshRef MeshRef => ShardRef.IsNone ? NodeRef : ShardRef;
    public readonly MeshNode? Node;
    public readonly bool IsLocal;
    public bool IsOffline => Node == null;

    public MeshRefTarget(MeshRef meshRef, MeshState meshState, MeshNode? ownNode)
    {
        if (meshRef.ShardRef.IsNone) {
            NodeRef = meshRef.NodeRef;
            if (NodeRef.IsNone)
                throw new ArgumentOutOfRangeException(nameof(meshRef));

            Node = meshState.NodeByRef.GetValueOrDefault(NodeRef);
        }
        else {
            ShardRef = meshRef.ShardRef;
            var shardMap = meshState.GetShardMap(ShardRef.Scheme);
            var meshNode = shardMap[ShardRef.Key];
            NodeRef = meshNode?.Ref ?? default;
            Node = NodeRef.IsNone ? null : meshState.NodeByRef.GetValueOrDefault(NodeRef);
        }
        IsLocal = ownNode != null && ReferenceEquals(Node, ownNode);
    }

    public override string ToString()
    {
        var source = ShardRef.IsNone ? "" : $"{ShardRef.Format()}->-";
        var target = NodeRef.Id.Value.NullIfEmpty() ?? "n/a";
        var offlinePrefix = ShardRef.IsNone && IsOffline ? "-offline" : "";
        var localPrefix = IsLocal ? "-local" : "";
        return ZString.Concat("@", source, target, offlinePrefix, localPrefix);
    }

    public bool IsChanged(MeshState meshState)
        => this != new MeshRefTarget(MeshRef, meshState, default);

    // Structural equality, IsLocal isn't used

    public bool Equals(MeshRefTarget other)
        => ShardRef.Equals(other.ShardRef)
            && NodeRef.Equals(other.NodeRef)
            && IsOffline == other.IsOffline;

    public override int GetHashCode()
        => HashCode.Combine(ShardRef, NodeRef, IsOffline);
}
