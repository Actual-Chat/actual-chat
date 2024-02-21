using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public abstract record QueuedCommand(Ulid Id) : IHasId<Ulid>
{
    private static readonly MethodInfo CreateFromCommandMethod = typeof(QueuedCommand)
        .GetMethod(nameof(CreateFromCommand), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly ConcurrentDictionary<Type, Func<Ulid, ICommand, QueuedCommand>> CommandFactoryMethods = new();

    public static IMomentClock Clock { get; set; } = MomentClockSet.Default.CoarseSystemClock;

    public abstract ICommand UntypedCommand { get; }
    public Moment CreatedAt => Id.Time;
    public Moment? StartedAt { get; init; }
    public Moment? CompletedAt { get; init; }
    public int TryIndex { get; init; }

    public static QueuedCommand New<T>(T command)
        where T : ICommand
    {
        var id = Ulid.NewUlid(Clock.UtcNow);
        var result = new QueuedCommand<T>(id, command);
        return result;
    }

    public static QueuedCommand FromCommand(Ulid ulid, ICommand command)
    {
        var factoryMethod = CommandFactoryMethods.GetOrAdd(command.GetType(),
            static t => (Func<Ulid, ICommand, QueuedCommand>)CreateFromCommandMethod
                .MakeGenericMethod(t)
                .Invoke(null, [])!);

        return factoryMethod(ulid, command);
    }

    // Equality is based solely on Id property
    public virtual bool Equals(QueuedCommand? other) => other != null && Id.Equals(other.Id);
    public override int GetHashCode() => Id.GetHashCode();

    private static Func<Ulid, ICommand, QueuedCommand> CreateFromCommand<T>() where T : ICommand
        => (ulid, command) => new QueuedCommand<T>(ulid, (T)command);
}

public sealed record QueuedCommand<T>(Ulid Id, T Command) : QueuedCommand(Id)
    where T: ICommand
{
    public override ICommand UntypedCommand => Command;
}
