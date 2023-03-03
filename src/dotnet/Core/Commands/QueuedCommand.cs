namespace ActualChat.Commands;

[DataContract]
public sealed record QueuedCommand(
    [property: DataMember] Symbol Id,
    [property: DataMember] long Version
) : IHasId<Symbol>
{
    public static IMomentClock Clock { get; set; }

    [DataMember] public ICommand Command { get; init; } = null!;
    [DataMember] public QueueId QueueId { get; init; }
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public Moment? StartedAt { get; init; }
    [DataMember] public Moment? CompletedAt { get; init; }
    [DataMember] public int TryIndex { get; init; }
    [DataMember] public string Error { get; init; } = "";

    static QueuedCommand()
        => Clock = MomentClockSet.Default.CoarseSystemClock;

    public static Symbol NewId()
        => Ulid.NewUlid().ToString();

    public static QueuedCommand New(ICommand command, QueuedCommandPriority priority = QueuedCommandPriority.Normal)
    {
        var now = Clock.Now;
        var id = NewId();
        var version = now.EpochOffsetTicks;
        // ReSharper disable once SuspiciousTypeConversion.Global
        var shardKey = command is IHasShardKey hasShardKey
            ? hasShardKey.ShardKey
            : command.GetHashCode();
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
