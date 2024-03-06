using ActualChat.Attributes;

namespace ActualChat;

public sealed class Queue(Symbol id, ShardScheme shardScheme) : IHasId<Symbol>
{
    private static readonly ConcurrentDictionary<object, CommandQueueAttribute?> _commandQueueAttributes = new();

    public static readonly Queue None = new(nameof(None), ShardScheme.None);
    public static readonly Queue Undefined = new(nameof(Undefined), ShardScheme.Undefined);
    public static readonly Queue Default = new(nameof(Default), ShardScheme.DefaultQueue);

    // A reverse map of QueueDef.Id to QueueDef
    public static readonly IReadOnlyDictionary<Symbol, Queue> ById = new Dictionary<Symbol, Queue>() {
        { None.Id, None },
        // { Undefined.Id, Undefined }, // Shouldn't be listed here
        { Default.Id, Default },
    };

    private string? _toString;

    public Symbol Id { get; } = id;
    public ShardScheme ShardScheme { get; } = shardScheme;
    public bool IsValid => ShardScheme.IsValid;
    public bool IsNone => ReferenceEquals(this, None);
    public bool IsUndefined => ReferenceEquals(this, Undefined);

    public override string ToString()
        => _toString ??= $"{nameof(Queue)}.{Id.Value}({ShardScheme})";

    public Queue? NullIfUndefined()
        => IsUndefined ? null : this;

    public static Queue? ForType<T>()
        => ForType(typeof(T));
    public static Queue? ForType(Type type)
    {
        var attr = _commandQueueAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<CommandQueueAttribute>()
                .SingleOrDefault(),
            type);
        var queue = attr != null ? ById[attr.QueueRole] : null;
        return queue ?? ForAssembly(type.Assembly);
    }

    // Private methods

    private static Queue? ForAssembly(Assembly assembly)
    {
        var attr = _commandQueueAttributes.GetOrAdd(assembly,
            static (_, t) => t
                .GetCustomAttributes<CommandQueueAttribute>()
                .SingleOrDefault(),
            assembly);
        return attr != null ? ById[attr.QueueRole] : null;
    }
}
