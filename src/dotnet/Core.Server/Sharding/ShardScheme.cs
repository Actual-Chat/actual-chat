using ActualChat.Attributes;
using ActualChat.Hosting;

namespace ActualChat;

#pragma warning disable CA1000

public sealed class ShardScheme(Symbol id, int shardCount, HostRole hostRole) : IHasId<Symbol>
{
    private static readonly ConcurrentDictionary<object, BackendClientAttribute?> _backendClientAttributes = new();

    public static readonly ShardScheme None = new(nameof(None), 0, HostRole.None);
    public static readonly ShardScheme Undefined = new(nameof(Undefined), 0, HostRole.None);
    public static readonly ShardScheme AnyServer = new(nameof(AnyServer), 10, HostRole.AnyServer); // Mostly for testing
    public static readonly ShardScheme MediaBackend = new(nameof(MediaBackend), 10, HostRole.MediaBackend);
    public static readonly ShardScheme AudioBackend = new(nameof(AudioBackend), 10, HostRole.AudioBackend);
    public static readonly ShardScheme ContactIndexingWorker = new(nameof(ContactIndexingWorker), 1, HostRole.ContactIndexingWorker);
    public static readonly ShardScheme DefaultQueue = new(nameof(DefaultQueue), 1, HostRole.DefaultQueue);
    public static readonly ShardScheme EventQueue = new(nameof(EventQueue), 1, HostRole.EventQueue);
    // A reverse map of ShardScheme.Id to ShardScheme
    public static readonly IReadOnlyDictionary<Symbol, ShardScheme> ById = new Dictionary<Symbol, ShardScheme>() {
        { None.Id, None },
        // { Undefined.Id, Undefined }, // Shouldn't be listed here
        { AnyServer.Id, AnyServer },
        { MediaBackend.Id, MediaBackend },
        { AudioBackend.Id, AudioBackend },
        { ContactIndexingWorker.Id, ContactIndexingWorker },
        // Queues
        { DefaultQueue.Id, DefaultQueue },
        { EventQueue.Id, EventQueue },
    };

    private string? _toString;

    public Symbol Id { get; } = id;
    public int ShardCount { get; } = shardCount;
    public HostRole HostRole { get; } = hostRole;
    public bool IsValid => ShardCount > 0;
    public bool IsNone => ReferenceEquals(this, None);
    public bool IsUndefined => ReferenceEquals(this, Undefined);

    public IEnumerable<int> ShardIndexes { get; } = Enumerable.Range(0, shardCount);

    public override string ToString()
        => _toString ??= $"{nameof(ShardScheme)}.{Id.Value}(x{ShardCount} @ {HostRole})";

    public ShardScheme? NullIfUndefined()
        => IsUndefined ? null : this;

    public static ShardScheme? ForType<T>()
        => ForType(typeof(T));
    public static ShardScheme? ForType(Type type)
    {
        var attr = _backendClientAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<BackendClientAttribute>()
                .SingleOrDefault(),
            type);
        var shardScheme = attr != null ? ById[attr.ShardScheme] : null;
        return shardScheme ?? ForAssembly(type.Assembly);
    }

    // Private methods

    private static ShardScheme? ForAssembly(Assembly assembly)
    {
        var attr = _backendClientAttributes.GetOrAdd(assembly,
            static (_, t) => t
                .GetCustomAttributes<BackendClientAttribute>()
                .SingleOrDefault(),
            assembly);
        return attr != null ? ById[attr.ShardScheme] : null;
    }
}
