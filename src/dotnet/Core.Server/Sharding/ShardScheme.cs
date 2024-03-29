using ActualChat.Attributes;
using ActualChat.Hosting;

namespace ActualChat;

#pragma warning disable CA1000

public sealed class ShardScheme(
    Symbol id,
    int shardCount,
    HostRole hostRole,
    ShardSchemeFlags flags = ShardSchemeFlags.Backend
    ) : IHasId<Symbol>
{
    private const int N = 12;
    private static readonly ConcurrentDictionary<object, BackendClientAttribute?> _backendClientAttributes = new();

    public static readonly ShardScheme None = new(nameof(None), 0, HostRole.None, ShardSchemeFlags.Special | ShardSchemeFlags.Queue);
    public static readonly ShardScheme Undefined = new(nameof(Undefined), 0, HostRole.None, ShardSchemeFlags.Special | ShardSchemeFlags.Queue);
    public static readonly ShardScheme EventQueue = new(nameof(EventQueue), N, HostRole.EventQueue, ShardSchemeFlags.Queue);
    public static readonly ShardScheme MediaBackend = new(nameof(MediaBackend), N, HostRole.MediaBackend);
    public static readonly ShardScheme AudioBackend = new(nameof(AudioBackend), N, HostRole.AudioBackend);
    public static readonly ShardScheme ChatBackend = new(nameof(ChatBackend), N, HostRole.ChatBackend);
    public static readonly ShardScheme ContactsBackend = new(nameof(ContactsBackend), N, HostRole.ContactsBackend);
    public static readonly ShardScheme ContactIndexerBackend = new(nameof(ContactIndexerBackend), 1, HostRole.ContactIndexerBackend);
    public static readonly ShardScheme InviteBackend = new(nameof(InviteBackend), 1, HostRole.InviteBackend);
    public static readonly ShardScheme NotificationBackend = new(nameof(NotificationBackend), N, HostRole.NotificationBackend);
    public static readonly ShardScheme SearchBackend = new(nameof(SearchBackend), N, HostRole.SearchBackend);
    public static readonly ShardScheme TranscriptionBackend = new(nameof(TranscriptionBackend), N, HostRole.TranscriptionBackend);
    public static readonly ShardScheme UsersBackend = new(nameof(UsersBackend), N, HostRole.UsersBackend);
    public static readonly ShardScheme TestBackend = new(nameof(TestBackend), N, HostRole.TestBackend); // Should be used only for testing

    // A reverse map of ShardScheme.Id to ShardScheme
    public static readonly IReadOnlyDictionary<Symbol, ShardScheme> ById = new Dictionary<Symbol, ShardScheme>() {
        { None.Id, None },
        // { Undefined.Id, Undefined }, // Shouldn't be listed here
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
    public ShardSchemeFlags Flags { get; } = flags;
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
            static (_, t) => t.GetCustomAttributes<BackendClientAttribute>().SingleOrDefault(),
            type);
        var shardScheme = attr != null ? ById[attr.ShardScheme] : null;
        return shardScheme ?? ForAssembly(type.Assembly);
    }

    // Private methods

    private static ShardScheme? ForAssembly(Assembly assembly)
    {
        var attr = _backendClientAttributes.GetOrAdd(assembly,
            static (_, t) => t.GetCustomAttributes<BackendClientAttribute>().SingleOrDefault(),
            assembly);
        return attr != null ? ById[attr.ShardScheme] : null;
    }
}
