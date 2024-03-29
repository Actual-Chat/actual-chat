using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Queues;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public abstract record QueuedCommand : IHasId<Ulid>
{
    private static readonly MethodInfo NewInternalMethod = typeof(QueuedCommand)
        .GetMethod(nameof(NewInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly ConcurrentDictionary<Type, Func<ICommand, Ulid, QueuedCommand>> Factories = new();

    public Ulid Id { get; init; }
    public abstract ICommand UntypedCommand { get; }
    public Moment CreatedAt => Id.Time;
    public Moment? StartedAt { get; init; }
    public Moment? CompletedAt { get; init; }
    public int TryIndex { get; init; }

    public static QueuedCommand New<TCommand>(TCommand command, Ulid id = default)
        where TCommand : ICommand
    {
        if (typeof(TCommand) != command.GetType())
            return NewUntyped(command, id);

        if (id == default)
            id = Ulid.NewUlid();
        var result = new QueuedCommand<TCommand>(command) { Id = id };
        return result;
    }

    public static QueuedCommand NewUntyped(ICommand command, Ulid id = default)
        => Factories.GetOrAdd(
            command.GetType(),
            static t => NewInternalMethod
                .MakeGenericMethod(t)
                .CreateDelegate<Func<ICommand, Ulid, QueuedCommand>>()
        ).Invoke(command, id);

    public static CommandKind GetKind(ICommand command)
        => command is IEventCommand eventCommand
            ? eventCommand.ChainId.IsEmpty
                ? CommandKind.UnboundEvent
                : CommandKind.BoundEvent
            : CommandKind.Command;

    // Equality is based solely on Id property
    public virtual bool Equals(QueuedCommand? other) => other != null && Id.Equals(other.Id);
    public override int GetHashCode() => Id.GetHashCode();

    // Private methods

    private static QueuedCommand NewInternal<T>(ICommand command, Ulid id = default)
        where T : ICommand
        => New((T)command, id);
}

public sealed record QueuedCommand<T>(T Command) : QueuedCommand
    where T: ICommand
{
    public override ICommand UntypedCommand => Command;
}
