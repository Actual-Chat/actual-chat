using Cysharp.Text;

namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
public readonly struct ShardRef(ShardScheme scheme, int key)
    : ICanBeNone<ShardRef>, IEquatable<ShardRef>
{
    public static ShardRef None => default;

    private readonly ShardScheme? _scheme = scheme;

    public ShardScheme Scheme => _scheme ?? ShardScheme.None;
    public int Key { get; } = key;

    // Computed properties
    public bool IsNone => _scheme == null || _scheme.IsNone;
    public bool IsValid => _scheme?.IsValid == true;

    public ShardRef(ShardScheme scheme, long key)
        : this(scheme, unchecked((int)key)) { }
    public ShardRef(int key)
        : this(ShardScheme.Undefined, key) { }
    public ShardRef(long key)
        : this(ShardScheme.Undefined, unchecked((int)key)) { }

    public void Deconstruct(out ShardScheme scheme, out int key)
    {
        scheme = Scheme;
        key = Key;
    }

    public override string ToString()
    {
        var shardScheme = Scheme;
        return shardScheme.IsNone
            ? $"{nameof(ShardRef)}.{nameof(None)}"
            : $"{shardScheme.Id.Value}[{Key.Format()} -> {TryGetShardIndex()?.Format() ?? "na"}/{shardScheme.ShardCount}]";
    }

    public string Format()
        => IsValid
            ? ZString.Concat(Scheme.Id.Value, "-S", GetShardIndex().Format())
            : Scheme.Id.Value;

    // Helpers

    public int? TryGetShardIndex() => Scheme.TryGetShardIndex(Key);
    public int GetShardIndex() => Scheme.GetShardIndex(Key);

    public ShardRef Normalize()
    {
        var shardScheme = Scheme;
        return shardScheme.IsNone ? this : new (shardScheme, shardScheme.GetShardIndex(Key));
    }

    public ShardRef WithSchemeIfUndefined(ShardScheme scheme)
    {
        var shardScheme = Scheme;
        return shardScheme.IsNone
            ? this
            : shardScheme.IsUndefined ? new(scheme, Key) : this;
    }

    public ShardRef RequireValid()
        => IsValid ? this
            : throw new ArgumentOutOfRangeException(null, $"Invalid {nameof(ShardRef)}: {this}.");

    // Equality

    public bool Equals(ShardRef other)
        => ReferenceEquals(Scheme, other.Scheme) && Key == other.Key;
    public override bool Equals(object? obj) => obj is ShardRef other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Scheme, Key);
    public static bool operator ==(ShardRef left, ShardRef right) => left.Equals(right);
    public static bool operator !=(ShardRef left, ShardRef right) => !left.Equals(right);
}
