using ActualChat.Mesh;
using Cysharp.Text;

namespace ActualChat;

/// <summary>
/// In fact, it's a resolved ShardRef/NodeRef - with cached Node, IsLocal, IsOffline, etc.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MeshRefTarget
{
    public readonly ShardRef ShardRef;
    public readonly NodeRef NodeRef;
    public MeshRef MeshRef => ShardRef.IsNone ? NodeRef : ShardRef;
    public readonly MeshNode? Node;
    public readonly bool IsLocal;
    public readonly MeshNodeState State;

    public MeshRefTarget(MeshRef meshRef, MeshState meshState, MeshNode? ownNode, Func<NodeRef, bool> isMarkedDeadFunc)
    {
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
        var isMarkedDead = isMarkedDeadFunc.Invoke(NodeRef);
        Node = isMarkedDead || NodeRef.IsNone
            ? null
            : meshState.NodeByRef.GetValueOrDefault(NodeRef);
        State = !ReferenceEquals(Node, null)
            ? MeshNodeState.Online
            : isMarkedDead && ShardRef.IsNone
                ? MeshNodeState.Dead
                : MeshNodeState.Offline;
        IsLocal = ownNode != null && ReferenceEquals(Node, ownNode);
    }

    public override string ToString()
    {
        var source = ShardRef.IsNone ? "" : $"{ShardRef.Format()}->-";
        var target = NodeRef.Id.Value.NullIfEmpty() ?? "n/a";
        var offlineSuffix = ShardRef.IsNone ? State.FormatSuffix() : "";
        var localSuffix = IsLocal ? "-local" : "";
        return ZString.Concat("@", source, target, offlineSuffix, localSuffix);
    }

    // Structural equality, IsLocal isn't used

    public bool Equals(MeshRefTarget other)
        => ShardRef.Equals(other.ShardRef)
            && NodeRef.Equals(other.NodeRef)
            && State == other.State;

    public override int GetHashCode()
        => HashCode.Combine(ShardRef, NodeRef, State);
}
