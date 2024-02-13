using ActualChat.Attributes;

namespace ActualChat.Hosting;

public static class HostRoles
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlySet<HostRole>> _cachedAssemblyRoles = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlySet<HostRole>> _cachedTypeRoles = new();

    public static IReadOnlySet<HostRole> App { get; }
        = new HashSet<HostRole>([ HostRole.App, HostRole.BlazorHost ]);

    public static class Server
    {
        private static readonly HashSet<HostRole> ParsableRoles = [
            HostRole.SingleServer,
            HostRole.FrontendServer,
            HostRole.BackendServer,
        ];
        private static readonly Dictionary<string, HostRole> ParsableRoleByValue = ParsableRoles
            .Select(x => new KeyValuePair<string, HostRole>(x.Value, x))
            .ToDictionary(StringComparer.OrdinalIgnoreCase);

        public static HostRole Parse(string? value)
            => value.IsNullOrEmpty() ? default
                : ParsableRoleByValue.GetValueOrDefault(value);

        public static HashSet<HostRole> GetAllRoles(HostRole role)
        {
            role = role.IsNone ? HostRole.SingleServer : role;
            var roles = new HashSet<HostRole>() { role };

            if (roles.Contains(HostRole.SingleServer)) {
                roles.Add(HostRole.FrontendServer);
                roles.Add(HostRole.BackendServer);
            }
            if (roles.Contains(HostRole.FrontendServer))
                roles.Add(HostRole.BlazorHost);
            if (roles.Contains(HostRole.BackendServer)) {
                roles.Add(HostRole.MediaBackendServer);
                roles.Add(HostRole.DefaultQueue);
            }
            return roles;
        }
    }

    public static IReadOnlySet<HostRole> GetServedByRoles(Assembly assembly)
        => _cachedAssemblyRoles.GetOrAdd(assembly, static a => a
            .GetCustomAttributes<ServedByRoleAttribute>()
            .Select(x => new HostRole(x.Role))
            .ToHashSet());

    public static IReadOnlySet<HostRole> GetServedByRoles(Type type)
        => _cachedTypeRoles.GetOrAdd(type, static t => {
            var typeRoles = t
                .GetCustomAttributes<ServedByRoleAttribute>()
                .Select(x => new HostRole(x.Role))
                .ToHashSet();
            return typeRoles.Count != 0 ? typeRoles : GetServedByRoles(t.Assembly);
        });
}
