using ActualChat.Attributes;

namespace ActualChat.Hosting;

public static class HostRoles
{
    private static readonly ConcurrentDictionary<Assembly, HostRole> _cachedAssemblyRoles = new();
    private static readonly ConcurrentDictionary<Type, HostRole> _cachedTypeRoles = new();
    private static readonly ConcurrentDictionary<HostRole, IReadOnlySet<HostRole>> _cachedAllRoles = new();


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

        public static IReadOnlySet<HostRole> GetAllRoles(HostRole role)
            => _cachedAllRoles.GetOrAdd(
                role,
                r => {
                    role = role.IsNone ? HostRole.OneServer : role;
                    var roles = new HashSet<HostRole> { role, HostRole.AnyServer };

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
                        roles.Add(HostRole.ChatBackend);
                        roles.Add(HostRole.ContactsBackend);
                        roles.Add(HostRole.InviteBackend);
                        roles.Add(HostRole.MediaBackend);
                        roles.Add(HostRole.NotificationBackend);
                        roles.Add(HostRole.SearchBackend);
                        roles.Add(HostRole.TranscriptionBackend);
                        roles.Add(HostRole.UsersBackend);
                        roles.Add(HostRole.ContactIndexingWorker);
                        roles.Add(HostRole.EventQueue);
                    }
                    return roles;
                });
    }

    public static class Queue
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

    public static HostRole GetCommandRole(Type commandType)
    {
        if (!typeof(ICommand).IsAssignableFrom(commandType))
            throw StandardError.NotSupported(commandType, $"{nameof(GetCommandRole)} provides result for commands only.");

        return _cachedTypeRoles.GetOrAdd(commandType,
            static t => {
                var commandBackendRoles = t
                    .GetCustomAttributes<BackendServiceAttribute>()
                    .Where(x => x.ServiceMode is ServiceMode.Server or ServiceMode.Local)
                    .Select(x => new HostRole(x.HostRole))
                    .ToList();
                var commandQueueRoles = t
                    .GetCustomAttributes<CommandQueueAttribute>()
                    .Select(x => new HostRole(x.QueueRole))
                    .ToList();
                if (commandBackendRoles.Any(r => !r.IsBackend))
                    throw StandardError.Configuration($"Command '{t.Name}' has invalid {nameof(BackendServiceAttribute)}.");

                if (commandQueueRoles.Any(r => !r.IsQueue))
                    throw StandardError.Configuration($"Command '{t.Name}' has invalid {nameof(CommandQueueAttribute)}.");

                if (commandBackendRoles.Count > 1)
                    throw StandardError.Configuration($"Command '{t.Name}' should have at most one '{nameof(BackendServiceAttribute)}' attribute.");

                if (commandQueueRoles.Count > 1)
                    throw StandardError.Configuration($"Command '{t.Name}' should have at most one '{nameof(CommandQueueAttribute)}' attribute.");

                return commandQueueRoles.Count != 0
                    ? commandQueueRoles[0]
                    : commandBackendRoles.Count != 0
                        ? commandBackendRoles[0]
                        : GetCommandRole(t.Assembly);
            });
    }

    private static HostRole GetCommandRole(Assembly assembly)
        => _cachedAssemblyRoles.GetOrAdd(assembly, static a => {
            var assemblyBackendRoles = a
                .GetCustomAttributes<BackendServiceAttribute>()
                .Where(x => x.ServiceMode is ServiceMode.Server)
                .Select(x => new HostRole(x.HostRole))
                .ToList();
            var assemblyQueueRoles = a
                .GetCustomAttributes<CommandQueueAttribute>()
                .Select(x => new HostRole(x.QueueRole))
                .ToList();

            if (assemblyBackendRoles.Any(r => !r.IsBackend))
                throw StandardError.Configuration($"Assembly '{a.FullName}' has invalid {nameof(BackendServiceAttribute)}.");

            if (assemblyQueueRoles.Any(r => !r.IsQueue))
                throw StandardError.Configuration($"Assembly '{a.FullName}' has invalid {nameof(CommandQueueAttribute)}.");

            if (assemblyBackendRoles.Count > 1)
                throw StandardError.Configuration($"Assembly '{a.FullName}' should have at most one '{nameof(BackendServiceAttribute)}' attribute with ServiceMode.Server.");

            if (assemblyQueueRoles.Count > 1)
                throw StandardError.Configuration($"Assembly '{a.FullName}' should have at most one '{nameof(CommandQueueAttribute)}' attribute.");

            return assemblyQueueRoles.Count != 0
                ? assemblyQueueRoles[0]
                : assemblyBackendRoles.Count != 0
                    ? assemblyBackendRoles[0]
                    : throw StandardError.Configuration(
                        $"Contract assembly '{a.FullName}' should have '{nameof(BackendServiceAttribute)}' attribute with ServiceMode.Server.");

        });
}
