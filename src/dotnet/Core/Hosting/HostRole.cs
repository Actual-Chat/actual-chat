namespace ActualChat.Hosting;

/// <summary>
/// Defines roles that a host can fulfill (e.g., Api, Backend services, Queues).
/// </summary>
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(ActualChat.Hosting.Internal.HostRoleMessagePackFormatter))]
public partial record struct HostRole(
    [property: DataMember(Order = 0), Key(0)] Symbol Id
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
    public static readonly HostRole BlazorHost = nameof(BlazorHost); // Used on both sides (server and client)

    // Actual backend roles
    public static readonly HostRole EventQueue = nameof(EventQueue);
    public static readonly HostRole FlowsBackend = nameof(FlowsBackend);
    public static readonly HostRole StreamingBackend = nameof(StreamingBackend);
    public static readonly HostRole LiveBackend = nameof(LiveBackend);
    public static readonly HostRole MediaBackend = nameof(MediaBackend);
    public static readonly HostRole ChatBackend = nameof(ChatBackend);
    public static readonly HostRole ContactsBackend = nameof(ContactsBackend);
    public static readonly HostRole InviteBackend = nameof(InviteBackend);
    public static readonly HostRole NotificationBackend = nameof(NotificationBackend);
    public static readonly HostRole SearchBackend = nameof(SearchBackend);
    public static readonly HostRole TranscriptionBackend = nameof(TranscriptionBackend);
    public static readonly HostRole UsersBackend = nameof(UsersBackend);
    public static readonly HostRole TestBackend = nameof(TestBackend);
    public static readonly HostRole DiagnosticsBackend = nameof(DiagnosticsBackend);

    // Queues
    public static readonly HostRole DefaultQueue = nameof(DefaultQueue);

    // The only role any app has
    public static readonly HostRole App = nameof(App); // Implies BlazorUI

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsQueue => Id.Value.EndsWith(QueueSuffix);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsBackend => Id == OneBackendServer.Id || Id.Value.EndsWith(BackendSuffix);

    public override string ToString() => Value;

    public static implicit operator HostRole(Symbol source) => new(source);
    public static implicit operator HostRole(string source) => new(source);
    public static implicit operator Symbol(HostRole source) => source.Id;

    // Comparison

    public int CompareTo(HostRole other)
        => string.CompareOrdinal(Id.Value, other.Id.Value);
}
