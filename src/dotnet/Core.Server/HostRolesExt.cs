using ActualChat.Attributes;
using ActualChat.Hosting;

namespace ActualChat;

public static class HostRolesExt
{
    private static readonly ConcurrentDictionary<object, BackendServiceAttribute[]> _backendServiceAttributes = new();
    private static readonly ConcurrentDictionary<object, CommandQueueAttribute?> _commandQueueAttributes = new();

    // GetServiceMode

    public static ServiceMode GetBackendServiceMode<T>(this IReadOnlySet<HostRole> hostRoles)
        => hostRoles.GetBackendServiceMode(typeof(T));
    public static ServiceMode GetBackendServiceMode(this IReadOnlySet<HostRole> hostRoles, Type type)
    {
        var attrs = _backendServiceAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<BackendServiceAttribute>()
                .OrderByDescending(x => x.Priority)
                .ToArray(),
            type);
        if (attrs.Length == 0)
            return hostRoles.GetBackendServiceMode(type.Assembly);

        var attr = attrs.FirstOrDefault(x => hostRoles.Contains(new HostRole(x.HostRole)));
        return attr?.ServiceMode ?? ServiceMode.Client;
    }

    // ShouldServe

    public static bool ShouldServe<T>(this IReadOnlySet<HostRole> hostRoles)
        => hostRoles.ShouldServe(typeof(T));
    public static bool ShouldServe(this IReadOnlySet<HostRole> hostRoles, Type type)
    {
        var attrs = _backendServiceAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<BackendServiceAttribute>()
                .OrderByDescending(x => x.Priority)
                .ToArray(),
            type);
        if (attrs.Length == 0)
            return hostRoles.ShouldServe(type.Assembly);

        var attr = attrs.FirstOrDefault(x => hostRoles.Contains(new HostRole(x.HostRole)));
        return attr is { ServiceMode: ServiceMode.Server };
    }

    public static bool ShouldServe(this IReadOnlySet<HostRole> hostRoles, Assembly assembly)
    {
        var attrs = _backendServiceAttributes.GetOrAdd(assembly,
            static (_, a) => a
                .GetCustomAttributes<BackendServiceAttribute>()
                .OrderByDescending(x => x.Priority)
                .ToArray(),
            assembly);
        if (attrs.Length == 0)
            throw StandardError.Configuration($"Assembly '{assembly.FullName}' defines event handlers and should have {nameof(BackendServiceAttribute)} attribute.");

        var attr = attrs.FirstOrDefault(x => hostRoles.Contains(new HostRole(x.HostRole)));
        return attr is { ServiceMode: ServiceMode.Server or ServiceMode.Mixed };
    }

    // Private methods

    private static ServiceMode GetBackendServiceMode(this IReadOnlySet<HostRole> hostRoles, Assembly assembly)
    {
        var attrs = _backendServiceAttributes.GetOrAdd(assembly,
            static (_, a) => a
                .GetCustomAttributes<BackendServiceAttribute>()
                .OrderByDescending(x => x.Priority)
                .ToArray(),
            assembly);
        var attr = attrs.FirstOrDefault(x => hostRoles.Contains(new HostRole(x.HostRole)));
        return attr?.ServiceMode ?? ServiceMode.Client;
    }
}
