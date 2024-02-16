using MemoryPack;

namespace ActualChat.Hosting;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record struct HostRole(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Symbol Id
    ) : ICanBeNone<HostRole>
{
    public static HostRole None => default;
    public static readonly HostRole SingleServer = nameof(SingleServer); // + FrontendServer, BackendServer
    public static readonly HostRole FrontendServer = nameof(FrontendServer); // + BlazorHost
    public static readonly HostRole BackendServer = nameof(BackendServer); // + MediaBackendServer, DefaultQueue
    public static readonly HostRole MediaBackendServer = nameof(MediaBackendServer);
    public static readonly HostRole DefaultQueue = nameof(DefaultQueue);

    // The only role any app has
    public static readonly HostRole App = nameof(App); // Implies BlazorUI

    // This implicit roles are used on both sides (server & client)
    public static readonly HostRole BlazorHost = nameof(BlazorHost);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsQueue => Id.Value.OrdinalEndsWith("Queue");

    public override string ToString() => Value;

    public static implicit operator HostRole(Symbol source) => new(source);
    public static implicit operator HostRole(string source) => new(source);
    public static implicit operator Symbol(HostRole source) => source.Id;
}
