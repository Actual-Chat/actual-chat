
namespace ActualChat.Queues;

/// <summary>
/// Reference to a queue identified by its shard scheme.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct QueueRef : ICanBeNone<QueueRef>, IEquatable<QueueRef>
{
    public static QueueRef None => default;
    public static readonly QueueRef Undefined = new(ShardScheme.Undefined);

    private readonly ShardScheme? _shardScheme;

    public ShardScheme ShardScheme => _shardScheme ?? ShardScheme.None;

    // Computed properties
    public bool IsNone => _shardScheme == null || _shardScheme.IsNone;
    public bool IsUndefined => _shardScheme is { IsUndefined: true };
    public bool IsValid => _shardScheme is { IsValid: true };

    public static QueueRef For(ICommand command, IServiceProvider services)
    {
        var commandKind = command.GetKind();
        if (commandKind is CommandKind.UnboundEvent) {
            // All unbound events go to the event queue, coz their execution = enqueuing their chains
            return ShardScheme.Queue;
        }

        if (command is IHasQueueRef hasQueueRef)
            return hasQueueRef.QueueRef; // Explicitly specified queue

        // Timeout-based heuristics
        return QueuesExt.GetTimeout(command, services) > QueuesExt.QueueTimeout
            ? ShardScheme.SlowQueue
            : ShardScheme.Queue;
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    public QueueRef(ShardScheme shardScheme)
    {
        if (!shardScheme.HasFlags(ShardSchemeFlags.Queue))
            throw new ArgumentOutOfRangeException(nameof(shardScheme),
                $"Specified ShardScheme doesn't have {nameof(ShardSchemeFlags.Queue)} flag.");
        _shardScheme = shardScheme;
    }

    public override string ToString()
    {
        var queue1 = ShardScheme;
        return queue1.IsNone
            ? $"{nameof(QueueRef)}.{nameof(None)}"
            : $"{nameof(QueueRef)}({Format()})";
    }

    public string Format()
        => ShardScheme.Name;

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
