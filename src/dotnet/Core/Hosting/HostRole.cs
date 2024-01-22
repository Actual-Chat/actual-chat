using MemoryPack;

namespace ActualChat.Hosting;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record struct HostRole(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Symbol Id)
{
    public static readonly HostRole SingleServer = nameof(SingleServer); // Implies WebApi
    public static readonly HostRole WebApi = nameof(WebApi); // Implies BlazorUI
    public static readonly HostRole AudioApi = nameof(WebApi);
    public static readonly HostRole UsersBackend = nameof(UsersBackend);
    public static readonly HostRole ChatsBackend = nameof(ChatsBackend);
    public static readonly HostRole Backend = nameof(Backend);

    // The only role any app has
    public static readonly HostRole App = nameof(App); // Implies BlazorUI

    // This implicit roles are used on both sides (server & client)
    public static readonly HostRole BlazorHost = nameof(BlazorHost);

    private static readonly HashSet<HostRole> ServerRoles = [
        SingleServer,
        WebApi,
        AudioApi,
        UsersBackend,
        ChatsBackend,
    ];
    private static readonly Dictionary<string, HostRole> ServerRoleByValueMap = ServerRoles
        .Select(x => new KeyValuePair<string, HostRole>(x.Value, x))
        .ToDictionary(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;

    public override string ToString() => Value;

    public static implicit operator HostRole(Symbol source) => new(source);
    public static implicit operator HostRole(string source) => new(source);

    public static IReadOnlySet<HostRole> GetClientRoles(AppKind appKind)
        => new HashSet<HostRole>() { App, BlazorHost };

    public static IReadOnlySet<HostRole> GetServerRoles(string roles)
    {
        const string rolesArgPrefix = "-roles:";
        roles = Environment.GetCommandLineArgs()
            .Skip(1)
            .Select(x => x.OrdinalStartsWith(rolesArgPrefix) ? x[rolesArgPrefix.Length..].Trim() : null)
            .FirstOrDefault(x => !x.IsNullOrEmpty())
            ?? roles;
        var roleSet = roles
            .Split(',')
            .Select(x => x.Trim())
            .Where(x => !x.IsNullOrEmpty())
            .Select(x => ServerRoleByValueMap.TryGetValue(x, out var role) ? role : (HostRole)x)
            .ToHashSet();
        AddImpliedServerRoles(roleSet);
        return roleSet;
    }

    public static HashSet<HostRole> AddImpliedServerRoles(HashSet<HostRole> roles)
    {
        if (roles.Count == 0)
            roles.Add(SingleServer);

        // Server roles
        if (roles.Contains(SingleServer)) {
            roles.Add(WebApi);
            roles.Add(UsersBackend);
            roles.Add(ChatsBackend);
        }
        if (roles.Contains(WebApi))
            roles.Add(BlazorHost);
        if (roles.Any(x => x.Value.OrdinalEndsWith("Backend")))
            roles.Add(Backend);
        return roles;
    }
}
