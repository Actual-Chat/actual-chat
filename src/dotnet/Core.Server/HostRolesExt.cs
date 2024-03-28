using ActualChat.Attributes;
using ActualChat.Hosting;

namespace ActualChat;

public static class HostRolesExt
{
    public static bool MustReplaceServerWithHybrid { get; set; }

    private static readonly ConcurrentDictionary<object, BackendServiceAttribute[]> _backendServiceAttributes = new();

    // GetServiceMode

    public static ServiceMode GetBackendServiceMode<T>(this IReadOnlySet<HostRole> hostRoles)
        => hostRoles.GetBackendServiceMode(typeof(T));
    public static ServiceMode GetBackendServiceMode(this IReadOnlySet<HostRole> hostRoles, Type type)
    {
        var attrs = _backendServiceAttributes.GetOrAdd(type,
            static (_, t) => t.GetCustomAttributes<BackendServiceAttribute>().OrderByDescending(x => x.Priority).ToArray(),
            type);
        if (attrs.Length == 0)
            return hostRoles.GetBackendServiceMode(type.Assembly);

        var attr = attrs.FirstOrDefault(x => hostRoles.Contains(new HostRole(x.HostRole)));
        return (attr?.ServiceMode ?? ServiceMode.Client).Fix();
    }

    // Private methods

    private static ServiceMode GetBackendServiceMode(this IReadOnlySet<HostRole> hostRoles, Assembly assembly)
    {
        var attrs = _backendServiceAttributes.GetOrAdd(assembly,
            static (_, a) => a.GetCustomAttributes<BackendServiceAttribute>().OrderByDescending(x => x.Priority).ToArray(),
            assembly);
        var attr = attrs.FirstOrDefault(x => hostRoles.Contains(new HostRole(x.HostRole)));
        return (attr?.ServiceMode ?? ServiceMode.Client).Fix();
    }

    private static ServiceMode Fix(this ServiceMode serviceMode)
    {
        if (MustReplaceServerWithHybrid && serviceMode is ServiceMode.Server)
            serviceMode = ServiceMode.Hybrid;
        return serviceMode;
    }
}
