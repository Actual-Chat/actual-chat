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

    public ShardRef ShardRef { get; }
    public NodeRef NodeRef { get; }

    // Computed properties
    public bool IsNone => ShardRef.IsNone && NodeRef.IsNone;
    public bool IsValid => ShardRef.IsValid || !NodeRef.IsNone;

    public MeshRefKind Kind => !ShardRef.IsNone ? MeshRefKind.ShardRef
        : NodeRef.IsNone ? MeshRefKind.None : MeshRefKind.NodeRef;

    public static MeshRef Node(NodeRef nodeRef)
        => new(nodeRef);
    public static MeshRef Shard(ShardRef shardRef)
        => new(shardRef);
    public static MeshRef Shard(ShardScheme scheme, int key)
        => new(new ShardRef(scheme, key));
    public static MeshRef Shard(ShardScheme scheme, long key)
        => new(new ShardRef(scheme, key));
    public static MeshRef Shard(int key)
        => new(new ShardRef(key));
    public static MeshRef Shard(long key)
        => new(new ShardRef(key));

    public MeshRef(ShardRef shardRef)
    {
        ShardRef = shardRef;
        NodeRef = default;
    }

    public MeshRef(NodeRef nodeRef)
    {
        NodeRef = nodeRef;
        ShardRef = default;
    }

    public void Deconstruct(out ShardRef shardRef, out NodeRef nodeRef)
    {
        shardRef = ShardRef;
        nodeRef = NodeRef;
    }

    // Conversion

    public override string ToString()
        => !ShardRef.IsNone ? $"@{ShardRef}"
            : !NodeRef.IsNone ? $"@{NodeRef}"
            : "@None";

    public static implicit operator MeshRef(NodeRef nodeRef) => new(nodeRef);
    public static implicit operator MeshRef(ShardRef shardRef) => new(shardRef);

    // Helpers

    public MeshRef Normalize()
        => ShardRef.IsNone ? this : ShardRef.Normalize();
    public MeshRef WithSchemeIfUndefined(ShardScheme scheme)
        => ShardRef.IsNone ? this : ShardRef.WithSchemeIfUndefined(scheme);

    // Equality

    public bool Equals(MeshRef other)
        => Equals(ShardRef, other.ShardRef) && Equals(NodeRef, other.NodeRef);
    public override bool Equals(object? obj) => obj is MeshRef other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ShardRef, NodeRef);
    public static bool operator ==(MeshRef left, MeshRef right) => left.Equals(right);
    public static bool operator !=(MeshRef left, MeshRef right) => !left.Equals(right);
}
