namespace ActualChat.Hosting;

public static class HostRoles
{
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
            var roles = new HashSet<HostRole>() { role, HostRole.AnyServer };

            if (roles.Contains(HostRole.SingleServer)) {
                roles.Add(HostRole.FrontendServer);
                roles.Add(HostRole.BackendServer);
            }
            if (roles.Contains(HostRole.FrontendServer))
                roles.Add(HostRole.BlazorHost);
            if (roles.Contains(HostRole.BackendServer)) {
                roles.Add(HostRole.AudioBackend);
                roles.Add(HostRole.MediaBackend);
                roles.Add(HostRole.DefaultQueue);
            }
            return roles;
        }
    }
}
