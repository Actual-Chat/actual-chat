namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
public readonly struct ShardRef(ShardScheme scheme, int key)
    : ICanBeNone<ShardRef>, IEquatable<ShardRef>
{
    public static ShardRef None => default;

    private readonly ShardScheme? _scheme = scheme;

    public ShardScheme Scheme => _scheme ?? ShardScheme.None.Instance;
    public int Key { get; } = key;

    // Computed properties
    public int Index => Scheme.GetShardIndex(Key);
    public bool IsNone => Scheme.IsNone;

    public ShardRef(ShardScheme scheme, long key)
        : this(scheme, unchecked((int)key)) { }
    public ShardRef(int key)
        : this(ShardScheme.Default.Instance, key) { }
    public ShardRef(long key)
        : this(ShardScheme.Default.Instance, unchecked((int)key)) { }

    public void Deconstruct(out ShardScheme scheme, out int key)
    {
        scheme = Scheme;
        key = Key;
    }

    public override string ToString()
        => $"{Scheme.Id}[{Key.Format()}]";

    // Helpers

    public ShardRef Normalize()
        => new(Scheme, Scheme.GetShardIndex(Key));
    public ShardRef WithNonDefaultSchemeOr(ShardScheme scheme)
        => Scheme.IsDefault ? new(scheme, Key) : this;
    public ShardRef WithNonDefaultSchemeOr(ShardScheme scheme, bool normalize)
    {
        scheme = Scheme.NonDefaultOr(scheme);
        return normalize
            ? new(scheme, scheme.GetShardIndex(Key))
            : new(scheme, Key);
    }

    // Equality

    public bool Equals(ShardRef other)
        => ReferenceEquals(Scheme, other.Scheme) && Key == other.Key;
    public override bool Equals(object? obj) => obj is ShardRef other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Scheme, Key);
    public static bool operator ==(ShardRef left, ShardRef right) => left.Equals(right);
    public static bool operator !=(ShardRef left, ShardRef right) => !left.Equals(right);
}
