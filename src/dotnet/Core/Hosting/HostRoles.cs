namespace ActualChat.Hosting;

public static class HostRoles
{
    private static readonly ConcurrentDictionary<(HostRole, bool), IReadOnlySet<HostRole>> _cachedAllRoles = new();

    public static IReadOnlySet<HostRole> App { get; }
        = new HashSet<HostRole>([ HostRole.App, HostRole.BlazorHost ]);

    public static class Server
    {
        private static readonly HashSet<HostRole> ParsableRoles = [
            HostRole.OneServer,
            HostRole.OneApiServer,
            HostRole.OneBackendServer,
            HostRole.ContactIndexerBackend,
        ];
        private static readonly Dictionary<string, HostRole> ParsableRoleByValue = ParsableRoles
            .Select(x => new KeyValuePair<string, HostRole>(x.Value, x))
            .ToDictionary(StringComparer.OrdinalIgnoreCase);

        public static HostRole Parse(string? value)
            => value.IsNullOrEmpty() ? default
                : ParsableRoleByValue.GetValueOrDefault(value);

        public static IReadOnlySet<HostRole> GetAllRoles(HostRole role, bool isTested = false)
            => _cachedAllRoles.GetOrAdd(
                (role, isTested),
                static key => {
                    var (role, isTested) = key;
                    role = role.IsNone ? HostRole.OneServer : role;
                    var roles = new HashSet<HostRole> { role, HostRole.AnyServer };

                    // OneServer roles
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
                        roles.Add(HostRole.EventQueue);
                        roles.Add(HostRole.AudioBackend);
                        roles.Add(HostRole.MediaBackend);
                        roles.Add(HostRole.ChatBackend);
                        roles.Add(HostRole.ContactsBackend);
                        roles.Add(HostRole.InviteBackend);
                        roles.Add(HostRole.MediaBackend);
                        roles.Add(HostRole.NotificationBackend);
                        roles.Add(HostRole.SearchBackend);
                        roles.Add(HostRole.TranscriptionBackend);
                        roles.Add(HostRole.UsersBackend);
                        roles.Add(HostRole.ContactIndexerBackend);
                        roles.Add(HostRole.MLSearchBackend);
                    }

                    // TestBackend
                    if (isTested)
                        roles.Add(HostRole.TestBackend);

                    return roles;
                });
    }

    public static class QueueRef
    {
        private static readonly HashSet<HostRole> ParsableRoles = [
            HostRole.DefaultQueue,
        ];
        private static readonly Dictionary<string, HostRole> ParsableRoleByValue = ParsableRoles
            .Select(x => new KeyValuePair<string, HostRole>(x.Value, x))
            .ToDictionary(StringComparer.OrdinalIgnoreCase);

        public static HostRole Parse(string? value)
            => value.IsNullOrEmpty() ? default
                : ParsableRoleByValue.GetValueOrDefault(value);
    }
}
