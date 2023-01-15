namespace ActualChat.Commands;

[DataContract]
public sealed record QueuedCommand(
    [property: DataMember] Symbol Id,
    [property: DataMember] long Version
) : IHasId<Symbol>
{
    private static readonly RecentlySeenMap<ICommand, QueuedCommand> _knownCommands;

    public static IMomentClock Clock { get; set; }

    [DataMember] public ICommand Command { get; init; } = null!;
    [DataMember] public QueueRef QueueRef { get; init; } = Queues.Default;
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public Moment? StartedAt { get; init; }
    [DataMember] public Moment? CompletedAt { get; init; }
    [DataMember] public int TryIndex { get; init; }
    [DataMember] public string Error { get; init; } = "";

    static QueuedCommand()
    {
        Clock = MomentClockSet.Default.CoarseSystemClock;
        _knownCommands = new RecentlySeenMap<ICommand, QueuedCommand>(1000, TimeSpan.FromHours(1), Clock);
    }

    public static Symbol NewId()
        => Ulid.NewUlid().ToString();

    public static QueuedCommand New(ICommand command, QueueRef queueRef = default)
    {
        lock (_knownCommands) {
            if (_knownCommands.TryGet(command, out var result))
                return result.WithQueueRef(queueRef);

            var now = Clock.Now;
            var id = NewId();
            var version = now.EpochOffsetTicks;
            result = new QueuedCommand(id, version) {
                Command = command,
                CreatedAt = now,
            };
            _knownCommands.TryAdd(command, result);
            return result;
        }
    }

    public QueuedCommand WithQueueRef(QueueRef queueRef)
        => QueueRef == queueRef ? this : this with { QueueRef = queueRef };

    // Equality is based solely on Id property
    public bool Equals(QueuedCommand? other) => other != null && Id.Equals(other.Id);
    public override int GetHashCode() => Id.GetHashCode();
}
