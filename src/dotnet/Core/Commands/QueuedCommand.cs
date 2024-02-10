using MemoryPack;

namespace ActualChat.Commands;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record QueuedCommand(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version
) : IHasId<Symbol>
{
    public static IMomentClock Clock { get; set; } = MomentClockSet.Default.CoarseSystemClock;

    // TODO(AK): ICommand serialization looks suspicious
    [DataMember, MemoryPackOrder(2)] [MemoryPackAllowSerialize] public ICommand Command { get; init; } = null!;
    [DataMember, MemoryPackOrder(3)] public QueueId QueueId { get; init; }
    [DataMember, MemoryPackOrder(4)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(5)] public Moment? StartedAt { get; init; }
    [DataMember, MemoryPackOrder(6)] public Moment? CompletedAt { get; init; }
    [DataMember, MemoryPackOrder(7)] public int TryIndex { get; init; }
    [DataMember, MemoryPackOrder(8)] public string Error { get; init; } = "";

    public static Symbol NewId()
        => Ulid.NewUlid().ToString();

    public static QueuedCommand New(ICommand command, QueuedCommandPriority priority = QueuedCommandPriority.Normal)
    {
        var now = Clock.Now;
        var id = NewId();
        var version = now.EpochOffsetTicks;
        // ReSharper disable once SuspiciousTypeConversion.Global

        var shardKeyResolver = ShardKeyResolvers.GetUntyped(command.GetType());
        var shardKey = shardKeyResolver?.Invoke(command) ?? command.GetHashCode();
        var result = new QueuedCommand(id, version) {
            Command = command,
            QueueId = new QueueId(shardKey, priority),
            CreatedAt = now,
        };
        return result;
    }

    // Equality is based solely on Id property
    public bool Equals(QueuedCommand? other) => other != null && Id.Equals(other.Id);
    public override int GetHashCode() => Id.GetHashCode();
}
