using Cysharp.Text;

namespace ActualChat.Queues;

[StructLayout(LayoutKind.Auto)]
public readonly struct QueueShardRef : ICanBeNone<QueueShardRef>, IEquatable<QueueShardRef>
{
    public static QueueShardRef None => default;

    public QueueRef QueueRef { get; }
    public int Key { get; }

    // Computed properties
    public ShardScheme ShardScheme => QueueRef.ShardScheme;
    public bool IsNone => QueueRef.IsNone;
    public bool IsUndefined => QueueRef.IsUndefined;
    public bool IsValid => QueueRef.IsValid;

    public QueueShardRef(int key)
        : this(QueueRef.Undefined, key) { }
    public QueueShardRef(long key)
        : this(QueueRef.Undefined, unchecked((int)key)) { }

    public QueueShardRef(QueueRef queueRef, long key)
        : this(queueRef, unchecked((int)key)) { }

    // ReSharper disable once ConvertToPrimaryConstructor
    public QueueShardRef(QueueRef queueRef, int key)
    {
        QueueRef = queueRef;
        Key = key;
    }

    public void Deconstruct(out QueueRef queueRef, out int key)
    {
        queueRef = QueueRef;
        key = Key;
    }

    public override string ToString()
        => QueueRef.IsNone
            ? $"{nameof(QueueShardRef)}.{nameof(None)}"
            : $"{QueueRef.Format()}[{Key.Format()} -> {TryGetShardIndex()?.Format() ?? "na"}/{ShardScheme.ShardCount}]";

    public string Format()
        => IsValid
            ? ZString.Concat(QueueRef.Format(), "-S", GetShardIndex().Format())
            : QueueRef.Format();

    // Helpers

    public int? TryGetShardIndex() => ShardScheme.TryGetShardIndex(Key);
    public int GetShardIndex() => ShardScheme.GetShardIndex(Key);

    public QueueShardRef Normalize()
        => QueueRef.IsNone ? None
            : new QueueShardRef(QueueRef, GetShardIndex());

    public QueueShardRef RequireValid()
        => IsValid ? this
            : throw new ArgumentOutOfRangeException(null, $"Invalid {nameof(QueueShardRef)}: {this}.");

    // Equality

    public bool Equals(QueueShardRef other)
        => QueueRef == other.QueueRef && Key == other.Key;
    public override bool Equals(object? obj) => obj is QueueShardRef other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(QueueRef, Key);
    public static bool operator ==(QueueShardRef left, QueueShardRef right) => left.Equals(right);
    public static bool operator !=(QueueShardRef left, QueueShardRef right) => !left.Equals(right);
}
