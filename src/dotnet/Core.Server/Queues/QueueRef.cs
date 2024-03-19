namespace ActualChat.Queues;

[StructLayout(LayoutKind.Auto)]
public readonly struct QueueRef : ICanBeNone<QueueRef>, IEquatable<QueueRef>
{
    public static QueueRef None => default;
    public static readonly QueueRef Undefined = new(ShardScheme.Undefined);

    private readonly ShardScheme? _shardScheme;

    public ShardScheme ShardScheme => _shardScheme ?? ShardScheme.None;

    // Computed properties
    public bool IsNone => _shardScheme == null || _shardScheme.IsNone;
    public bool IsUndefined => _shardScheme != null && _shardScheme.IsUndefined;
    public bool IsValid => _shardScheme != null && _shardScheme.IsValid;

    // ReSharper disable once ConvertToPrimaryConstructor
    public QueueRef(ShardScheme shardScheme)
        => _shardScheme = shardScheme;

    public override string ToString()
    {
        var queue1 = ShardScheme;
        return queue1.IsNone
            ? $"{nameof(QueueRef)}.{nameof(None)}"
            : $"{nameof(QueueRef)}({Format()})";
    }

    public string Format()
        => ShardScheme.Id.Value;

    public static implicit operator QueueRef(ShardScheme shardScheme) => new(shardScheme);

    // Helpers

    public QueueRef RequireValid()
        => IsValid ? this
            : throw new ArgumentOutOfRangeException(null, $"Invalid {nameof(QueueRef)}: {this}.");

    // Equality

    public bool Equals(QueueRef other) => ReferenceEquals(ShardScheme, other.ShardScheme);
    public override bool Equals(object? obj) => obj is QueueRef other && Equals(other);
    public override int GetHashCode() => ShardScheme.GetHashCode();
    public static bool operator ==(QueueRef left, QueueRef right) => left.Equals(right);
    public static bool operator !=(QueueRef left, QueueRef right) => !left.Equals(right);
}
