using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualLab.Internal;

namespace ActualChat.Sharding;

#pragma warning disable CA1000

public sealed class ShardScheme(
    Symbol id,
    int shardCount,
    HostRole hostRole,
    ShardSchemeFlags flags = ShardSchemeFlags.Backend
    ) : IHasId<Symbol>
{
    private const int N = 12;
    private static readonly ConcurrentDictionary<object, BackendClientAttribute?> BackendClientAttributes = new();

    public static readonly ShardScheme None = new(nameof(None), 0, HostRole.None, ShardSchemeFlags.Special | ShardSchemeFlags.Queue);
    public static readonly ShardScheme Undefined = new(nameof(Undefined), 0, HostRole.None, ShardSchemeFlags.Special | ShardSchemeFlags.Queue);
    public static readonly ShardScheme EventQueue = new(nameof(EventQueue), N, HostRole.EventQueue, ShardSchemeFlags.Queue);
    public static readonly ShardScheme SummarizeQueue = new(nameof(SummarizeQueue), 1, HostRole.EventQueue, ShardSchemeFlags.SlowQueue);
    public static readonly ShardScheme FlowsBackend = new(nameof(FlowsBackend), N, HostRole.FlowsBackend);
    public static readonly ShardScheme MediaBackend = new(nameof(MediaBackend), N, HostRole.MediaBackend);
    public static readonly ShardScheme AudioBackend = new(nameof(AudioBackend), N, HostRole.AudioBackend);
    public static readonly ShardScheme ChatBackend = new(nameof(ChatBackend), N, HostRole.ChatBackend);
    public static readonly ShardScheme ContactsBackend = new(nameof(ContactsBackend), N, HostRole.ContactsBackend);
    public static readonly ShardScheme InviteBackend = new(nameof(InviteBackend), 1, HostRole.InviteBackend);
    public static readonly ShardScheme NotificationBackend = new(nameof(NotificationBackend), N, HostRole.NotificationBackend);
    public static readonly ShardScheme SearchBackend = new(nameof(SearchBackend), N, HostRole.SearchBackend);
    public static readonly ShardScheme MLSearchBackend = new(nameof(MLSearchBackend), N, HostRole.MLSearchBackend);
    public static readonly ShardScheme TranscriptionBackend = new(nameof(TranscriptionBackend), N, HostRole.TranscriptionBackend);
    public static readonly ShardScheme UsersBackend = new(nameof(UsersBackend), N, HostRole.UsersBackend);
    public static readonly ShardScheme TestBackend = new(nameof(TestBackend), N, HostRole.TestBackend); // Should be used only for testing

    // A reverse map of ShardScheme.Id to ShardScheme
    public static readonly IReadOnlyDictionary<Symbol, ShardScheme> ById = new Dictionary<Symbol, ShardScheme>() {
        { None.Id, None },
        // { Undefined.Id, Undefined }, // Shouldn't be listed here
        { EventQueue.Id, EventQueue },
        { SummarizeQueue.Id, SummarizeQueue },
        { MediaBackend.Id, MediaBackend },
        { AudioBackend.Id, AudioBackend },
        { ChatBackend.Id, ChatBackend },
        { ContactsBackend.Id, ContactsBackend },
        { FlowsBackend.Id, FlowsBackend },
        { InviteBackend.Id, InviteBackend },
        { NotificationBackend.Id, NotificationBackend },
        { SearchBackend.Id, SearchBackend },
        { MLSearchBackend.Id, MLSearchBackend },
        { TranscriptionBackend.Id, TranscriptionBackend },
        { UsersBackend.Id, UsersBackend },
        { TestBackend.Id, TestBackend },
    };
    public static readonly IReadOnlyDictionary<Symbol, ShardScheme> ByBackendHostRole
        = ById.Values
            .Where(x => x.IsNone || x.HostRole.IsBackend)
            .Select(x => KeyValuePair.Create(x.HostRole.Id, x))
            .Append(new KeyValuePair<Symbol, ShardScheme>(nameof(HostRole.None), None))
            .ToDictionary();

    private string? _toString;

    public Symbol Id { get; } = id;
    public string Name { get; } = id.Value; // A shortcut
    public int ShardCount { get; } = shardCount;
    public HostRole HostRole { get; } = hostRole;
    public ShardSchemeFlags Flags { get; } = flags;
    public bool IsValid => ShardCount > 0;
    public bool IsNone => ReferenceEquals(this, None);
    public bool IsUndefined => ReferenceEquals(this, Undefined);

    public IEnumerable<int> ShardIndexes { get; } = Enumerable.Range(0, shardCount);

    public int? DegreeOfParallelism { get; init; } = null;

    public override string ToString()
        => _toString ??= $"{nameof(ShardScheme)}.{Name}(x{ShardCount} @ {HostRole})";

    public ShardScheme? NullIfUndefined()
        => IsUndefined ? null : this;

    public static ShardScheme? ForType(Type type)
    {
        if (!type.IsInterface)
            throw Errors.MustBeInterface(type, nameof(type));

        var attr = BackendClientAttributes.GetOrAdd(type,
            static (_, t) => t.GetCustomAttributes<BackendClientAttribute>().SingleOrDefault(),
            type);
        var shardScheme = attr != null ? ByBackendHostRole[attr.HostRole] : null;
        return shardScheme ?? ForAssembly(type.Assembly);
    }

    // Private methods

    private static ShardScheme? ForAssembly(Assembly assembly)
    {
        var attr = BackendClientAttributes.GetOrAdd(assembly,
            static (_, t) => t.GetCustomAttributes<BackendClientAttribute>().SingleOrDefault(),
            assembly);
        return attr != null ? ByBackendHostRole[attr.HostRole] : null;
    }
}
