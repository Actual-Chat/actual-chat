namespace ActualChat;

public enum MeshRefKind
{
    None,
    NodeRef,
    ShardRef,
}

[StructLayout(LayoutKind.Auto)]
public readonly struct MeshRef : ICanBeNone<MeshRef>, IEquatable<MeshRef>
{
    public static MeshRef None => default;

    public MeshShardRef ShardRef { get; }
    public MeshNodeId NodeRef { get; }
    public bool IsNone => ShardRef.IsNone && NodeRef.IsNone;

    public MeshRefKind Kind => !ShardRef.IsNone
        ? MeshRefKind.ShardRef
        : NodeRef.IsNone ? MeshRefKind.None : MeshRefKind.NodeRef;

    public static MeshRef Node(MeshNodeId nodeRef)
        => new(nodeRef);
    public static MeshRef Shard(MeshShardRef shardRef)
        => new(shardRef);
    public static MeshRef Shard(ShardScheme shardScheme, int shardKey)
        => new(new MeshShardRef(shardScheme, shardKey));

    public MeshRef(MeshShardRef shardRef)
    {
        ShardRef = shardRef;
        NodeRef = default;
    }

    public MeshRef(MeshNodeId nodeRef)
    {
        NodeRef = nodeRef;
        ShardRef = default;
    }

    // Conversion

    public override string ToString()
        => !ShardRef.IsNone ? $"@{ShardRef}"
            : !NodeRef.IsNone ? $"@{NodeRef}"
            : "@None";

    // Equality

    public bool Equals(MeshRef other)
        => Equals(ShardRef, other.ShardRef) && Equals(NodeRef, other.NodeRef);
    public override bool Equals(object? obj) => obj is MeshRef other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ShardRef, NodeRef);
    public static bool operator ==(MeshRef left, MeshRef right) => left.Equals(right);
    public static bool operator !=(MeshRef left, MeshRef right) => !left.Equals(right);
}
