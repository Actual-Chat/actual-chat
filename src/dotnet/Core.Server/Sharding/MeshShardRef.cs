namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
public readonly struct MeshShardRef(ShardScheme shardScheme, int shardKey)
    : ICanBeNone<MeshShardRef>, IEquatable<MeshShardRef>
{
    public static MeshShardRef None => default;

    private readonly ShardScheme? _shardScheme = shardScheme;

    public ShardScheme ShardScheme => _shardScheme ?? ShardScheme.None.Instance;
    public int ShardKey { get; } = shardKey;
    public bool IsNone => ShardScheme.IsNone;

    public override string ToString()
        => $"{ShardScheme.Id}[{ShardKey}]";

    // Equality

    public bool Equals(MeshShardRef other)
        => ReferenceEquals(ShardScheme, other.ShardScheme) && ShardKey == other.ShardKey;
    public override bool Equals(object? obj) => obj is MeshShardRef other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ShardScheme, ShardKey);
    public static bool operator ==(MeshShardRef left, MeshShardRef right) => left.Equals(right);
    public static bool operator !=(MeshShardRef left, MeshShardRef right) => !left.Equals(right);
}
