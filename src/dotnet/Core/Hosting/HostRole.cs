using MemoryPack;

namespace ActualChat.Hosting;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record struct HostRole(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Symbol Id
    ) : ICanBeNone<HostRole>, IComparable<HostRole>
{
    public const string QueueSuffix = "Queue";
    public const string BackendSuffix = "Backend";

    public static HostRole None => default;

    // Meta / root roles: the only ones you can use to start a host
    public static readonly HostRole AnyServer = nameof(AnyServer); // Any server has it
    public static readonly HostRole OneServer = nameof(OneServer); // + OneFrontendServer, OneBackendServer
    public static readonly HostRole OneApiServer = nameof(OneApiServer); // + Api
    public static readonly HostRole OneBackendServer = nameof(OneBackendServer); // + XxxBackend, DefaultQueue

    // Actual front-end roles
    public static readonly HostRole Api = nameof(Api); // + BlazorHost
    public static readonly HostRole BlazorHost = nameof(BlazorHost); // Used on both sides (server & client)

    // Actual backend roles
    public static readonly HostRole EventQueue = nameof(EventQueue);
    public static readonly HostRole FlowsBackend = nameof(FlowsBackend);
    public static readonly HostRole AudioBackend = nameof(AudioBackend);
    public static readonly HostRole MediaBackend = nameof(MediaBackend);
    public static readonly HostRole ChatBackend = nameof(ChatBackend);
    public static readonly HostRole ContactsBackend = nameof(ContactsBackend);
    public static readonly HostRole ContactIndexerBackend = nameof(ContactIndexerBackend);
    public static readonly HostRole InviteBackend = nameof(InviteBackend);
    public static readonly HostRole NotificationBackend = nameof(NotificationBackend);
    public static readonly HostRole SearchBackend = nameof(SearchBackend);
    public static readonly HostRole TranscriptionBackend = nameof(TranscriptionBackend);
    public static readonly HostRole UsersBackend = nameof(UsersBackend);
    public static readonly HostRole MLSearchBackend = nameof(MLSearchBackend);
    public static readonly HostRole TestBackend = nameof(TestBackend);

    // Queues
    public static readonly HostRole DefaultQueue = nameof(DefaultQueue);

    // The only role any app has
    public static readonly HostRole App = nameof(App); // Implies BlazorUI

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsQueue => Id.Value.OrdinalEndsWith(QueueSuffix);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsBackend => Id == OneBackendServer.Id || Id.Value.OrdinalEndsWith(BackendSuffix);

    public override string ToString() => Value;

    public static implicit operator HostRole(Symbol source) => new(source);
    public static implicit operator HostRole(string source) => new(source);
    public static implicit operator Symbol(HostRole source) => source.Id;

    // Comparison

    public int CompareTo(HostRole other)
        => string.CompareOrdinal(Id.Value, other.Id.Value);
}
