using System.Collections.Frozen;
using Microsoft.Extensions.Primitives;

namespace ActualChat.Queues;

public abstract record QueuedCommand : IHasUuid, IHasId<string>
{
    private static readonly MethodInfo NewInternalMethod
        = typeof(QueuedCommand).GetMethod(nameof(NewInternal), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly ConcurrentDictionary<Type, Func<ICommand, string?, IReadOnlyDictionary<string, StringValues>?, QueuedCommand>> Factories
        = new();

    public string Uuid { get; private init; } = "";
    string IHasId<string>.Id => Uuid;

    public IReadOnlyDictionary<string, StringValues> Headers { get; init; } = FrozenDictionary<string, StringValues>.Empty;
    public abstract ICommand UntypedCommand { get; }
    public Moment CreatedAt { get; init; } = Moment.Now;
    public Moment? StartedAt { get; init; }
    public Moment? CompletedAt { get; init; }
    public int TryIndex { get; init; }

    public static QueuedCommand New<TCommand>(
        TCommand command,
        string? uuid = null,
        IReadOnlyDictionary<string, StringValues>? headers = null)
        where TCommand : ICommand
    {
        if (typeof(TCommand) != command.GetType())
            return NewUntyped(command, uuid, headers);

        if (uuid.IsNullOrEmpty()) {
            if (command is IHasUuid hasUuid)
                uuid = hasUuid.Uuid;
            else
                uuid = Ulid.NewUlid().ToString();
        }

        return headers is null
            ? new QueuedCommand<TCommand>(command) { Uuid = uuid }
            : new QueuedCommand<TCommand>(command) { Uuid = uuid, Headers = headers };
    }

    public static QueuedCommand NewUntyped(
        ICommand command,
        string? uuid = null,
        IReadOnlyDictionary<string, StringValues>? headers = null)
        => Factories.GetOrAdd(
            command.GetType(),
            static t => NewInternalMethod
                .MakeGenericMethod(t)
                .CreateDelegate<Func<ICommand, string?, IReadOnlyDictionary<string, StringValues>?, QueuedCommand>>()
        ).Invoke(command, uuid, headers);

    public static CommandKind GetKind(ICommand command)
        => command is IEventCommand eventCommand
            ? eventCommand.ChainId.IsNullOrEmpty()
                ? CommandKind.UnboundEvent
                : CommandKind.BoundEvent
            : CommandKind.Command;

    // This record relies on referential equality
    public virtual bool Equals(QueuedCommand? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    // Private methods

    private static QueuedCommand NewInternal<T>(
        ICommand command,
        string? uuid = null,
        IReadOnlyDictionary<string, StringValues>? headers = null)
        where T : ICommand
        => New((T)command, uuid, headers);
}

public sealed record QueuedCommand<T>(T Command) : QueuedCommand
    where T: ICommand
{
    public override ICommand UntypedCommand => Command;
}
