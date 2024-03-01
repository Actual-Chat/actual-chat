using ActualChat.Attributes;
using ActualChat.Hosting;

namespace ActualChat;

public static class HostRolesExt
{
    private static readonly ConcurrentDictionary<object, BackendServiceAttribute[]> _backendServiceAttributes = new();
    private static readonly ConcurrentDictionary<object, DefaultQueueAttribute?> _defaultQueueAttributes = new();

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
        var attr = attrs.FirstOrDefault(x => hostRoles.Contains(new HostRole(x.HostRole)));
        return attr is { ServiceMode: ServiceMode.Server };
    }

    // GetBackendClientRole

    public static HostRole? GetBackendClientRole<T>(this IReadOnlySet<HostRole> hostRoles)
        => hostRoles.GetBackendClientRole(typeof(T));
    public static HostRole? GetBackendClientRole(this IReadOnlySet<HostRole> hostRoles, Type type)
    {
        var attr = _backendClientAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<BackendClientAttribute>()
                .SingleOrDefault(),
            type);
        var hostRole = attr != null ? (HostRole)attr.HostedByRole : (HostRole?)null;
        return hostRole ?? hostRoles.GetBackendClientRole(type.Assembly);
    }

    public static HostRole? GetBackendClientRole(this IReadOnlySet<HostRole> hostRoles, Assembly assembly)
    {
        var attr = _backendClientAttributes.GetOrAdd(assembly,
            static (_, t) => t
                .GetCustomAttributes<BackendClientAttribute>()
                .SingleOrDefault(),
            assembly);
        var hostRole = attr != null ? (HostRole)attr.HostedByRole : (HostRole?)null;
        return hostRole;
    }

    // GetCommandQueue

    public static HostRole? GetCommandQueueRole<T>(this IReadOnlySet<HostRole> hostRoles)
        => hostRoles.GetCommandQueueRole(typeof(T));
    public static HostRole? GetCommandQueueRole(this IReadOnlySet<HostRole> hostRoles, Type type)
    {
        var attr = _defaultQueueAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<CommandQueueAttribute>()
                .SingleOrDefault(),
            type);
        var hostRole = attr != null ? (HostRole)attr.QueueRole : (HostRole?)null;
        if (hostRole is { IsQueue: false })
            throw StandardError.Internal($"Type '{type.FullName}' has invalid {nameof(CommandQueueAttribute)}.");

        return hostRole ?? hostRoles.GetBackendClientRole(type.Assembly);
    }

    public static HostRole? GetCommandQueueRole(this IReadOnlySet<HostRole> hostRoles, Assembly assembly)
    {
        var attr = _defaultQueueAttributes.GetOrAdd(assembly,
            static (_, t) => t
                .GetCustomAttributes<CommandQueueAttribute>()
                .SingleOrDefault(),
            assembly);
        var hostRole = attr != null ? (HostRole)attr.QueueRole : (HostRole?)null;
        if (hostRole is { IsQueue: false })
            throw StandardError.Internal($"Assembly '{assembly.FullName}' has invalid {nameof(CommandQueueAttribute)}.");

        return hostRole;
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
