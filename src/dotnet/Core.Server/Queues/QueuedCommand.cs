using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Primitives;

namespace ActualChat.Queues;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public abstract record QueuedCommand : IHasId<Ulid>
{
    private static readonly MethodInfo NewInternalMethod = typeof(QueuedCommand)
        .GetMethod(nameof(NewInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly ConcurrentDictionary<Type, Func<ICommand, Ulid, IReadOnlyDictionary<string, StringValues>?, QueuedCommand>> Factories = new();

    public Ulid Id { get; init; }
    public IReadOnlyDictionary<string, StringValues> Headers { get; init; } = FrozenDictionary<string, StringValues>.Empty;
    public abstract ICommand UntypedCommand { get; }
    public Moment CreatedAt => Id.Time;
    public Moment? StartedAt { get; init; }
    public Moment? CompletedAt { get; init; }
    public int TryIndex { get; init; }

    public static QueuedCommand New<TCommand>(
        TCommand command,
        Ulid id = default,
        IReadOnlyDictionary<string, StringValues>? headers = default)
        where TCommand : ICommand
    {
        if (typeof(TCommand) != command.GetType())
            return NewUntyped(command, id, headers);

        if (id == default)
            id = Ulid.NewUlid();

        return headers is null
            ? new QueuedCommand<TCommand>(command) { Id = id }
            : new QueuedCommand<TCommand>(command) { Id = id, Headers = headers };
    }

    public static QueuedCommand NewUntyped(
        ICommand command,
        Ulid id = default,
        IReadOnlyDictionary<string, StringValues>? headers = default)
        => Factories.GetOrAdd(
            command.GetType(),
            static t => NewInternalMethod
                .MakeGenericMethod(t)
                .CreateDelegate<Func<ICommand, Ulid, IReadOnlyDictionary<string, StringValues>?, QueuedCommand>>()
        ).Invoke(command, id, headers);

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

    private static QueuedCommand NewInternal<T>(
        ICommand command,
        Ulid id = default,
        IReadOnlyDictionary<string, StringValues>? headers = default)
        where T : ICommand
        => New((T)command, id, headers);
}

public sealed record QueuedCommand<T>(T Command) : QueuedCommand
    where T: ICommand
{
    public override ICommand UntypedCommand => Command;
}
