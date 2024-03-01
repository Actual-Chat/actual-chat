namespace ActualChat.Hosting;

public static class HostRoles
{
    public static IReadOnlySet<HostRole> App { get; }
        = new HashSet<HostRole>([ HostRole.App, HostRole.BlazorHost ]);

    public static class Server
    {
        private static readonly HashSet<HostRole> ParsableRoles = [
            HostRole.OneServer,
            HostRole.OneApiServer,
            HostRole.OneBackendServer,
            HostRole.ContactIndexingWorker,
        ];
        private static readonly Dictionary<string, HostRole> ParsableRoleByValue = ParsableRoles
            .Select(x => new KeyValuePair<string, HostRole>(x.Value, x))
            .ToDictionary(StringComparer.OrdinalIgnoreCase);

        public static HostRole Parse(string? value)
            => value.IsNullOrEmpty() ? default
                : ParsableRoleByValue.GetValueOrDefault(value);

        public static HashSet<HostRole> GetAllRoles(HostRole role)
        {
            role = role.IsNone ? HostRole.OneServer : role;
            var roles = new HashSet<HostRole>() { role, HostRole.AnyServer };

            if (roles.Contains(HostRole.OneServer)) {
                roles.Add(HostRole.OneApiServer);
                roles.Add(HostRole.OneBackendServer);
            }

            // Api roles
            if (roles.Contains(HostRole.OneApiServer))
                roles.Add(HostRole.Api);
            if (roles.Contains(HostRole.Api))
                roles.Add(HostRole.BlazorHost);

            // Backend roles
            if (roles.Contains(HostRole.OneBackendServer)) {
                roles.Add(HostRole.AudioBackend);
                roles.Add(HostRole.MediaBackend);
                roles.Add(HostRole.ContactIndexingWorker);
                roles.Add(HostRole.DefaultQueue);
            }
            return roles;
        }
    }
}
