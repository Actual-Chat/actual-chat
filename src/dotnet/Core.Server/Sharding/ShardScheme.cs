using ActualChat.Attributes;
using ActualChat.Hosting;

namespace ActualChat;

#pragma warning disable CA1000

public sealed class ShardScheme(Symbol id, int shardCount, HostRole hostRole) : IHasId<Symbol>
{
    private static readonly ConcurrentDictionary<object, BackendClientAttribute?> _backendClientAttributes = new();

    public static readonly ShardScheme None = new(nameof(None), 0, HostRole.None);
    public static readonly ShardScheme Undefined = new(nameof(Undefined), 0, HostRole.None);
    public static readonly ShardScheme EventQueue = new(nameof(EventQueue), 10, HostRole.EventQueue);
    public static readonly ShardScheme MediaBackend = new(nameof(MediaBackend), 10, HostRole.MediaBackend);
    public static readonly ShardScheme AudioBackend = new(nameof(AudioBackend), 10, HostRole.AudioBackend);
    public static readonly ShardScheme ChatBackend = new(nameof(ChatBackend), 30, HostRole.ChatBackend);
    public static readonly ShardScheme ContactsBackend = new(nameof(ContactsBackend), 10, HostRole.ContactsBackend);
    public static readonly ShardScheme ContactIndexerBackend = new(nameof(ContactIndexerBackend), 1, HostRole.ContactIndexerBackend);
    public static readonly ShardScheme InviteBackend = new(nameof(InviteBackend), 1, HostRole.InviteBackend);
    public static readonly ShardScheme NotificationBackend = new(nameof(NotificationBackend), 10, HostRole.NotificationBackend);
    public static readonly ShardScheme SearchBackend = new(nameof(SearchBackend), 10, HostRole.SearchBackend);
    public static readonly ShardScheme TranscriptionBackend = new(nameof(TranscriptionBackend), 10, HostRole.TranscriptionBackend);
    public static readonly ShardScheme UsersBackend = new(nameof(UsersBackend), 10, HostRole.UsersBackend);
    public static readonly ShardScheme TestBackend = new(nameof(TestBackend), 10, HostRole.TestBackend); // Should be used only for testing

    // A reverse map of ShardScheme.Id to ShardScheme
    public static readonly IReadOnlyDictionary<Symbol, ShardScheme> ById = new Dictionary<Symbol, ShardScheme>() {
        { None.Id, None },
        // { Undefined.Id, Undefined }, // Shouldn't be listed here
        // { AnyServer.Id, AnyServer }, // Shouldn't be listed here
        { EventQueue.Id, EventQueue },
        { MediaBackend.Id, MediaBackend },
        { AudioBackend.Id, AudioBackend },
        { ChatBackend.Id, ChatBackend },
        { ContactsBackend.Id, ContactsBackend },
        { InviteBackend.Id, InviteBackend },
        { NotificationBackend.Id, NotificationBackend },
        { SearchBackend.Id, SearchBackend },
        { TranscriptionBackend.Id, TranscriptionBackend },
        { UsersBackend.Id, UsersBackend },
        { ContactIndexerBackend.Id, ContactIndexerBackend },
        { TestBackend.Id, TestBackend },
    };

    private string? _toString;

    public Symbol Id { get; } = id;
    public int ShardCount { get; } = shardCount;
    public HostRole HostRole { get; } = hostRole;
    public bool IsValid => ShardCount > 0;
    public bool IsNone => ReferenceEquals(this, None);
    public bool IsUndefined => ReferenceEquals(this, Undefined);
    public bool IsQueue => Id.Value.OrdinalEndsWith("Queue");

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
