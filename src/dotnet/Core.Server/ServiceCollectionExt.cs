using ActualChat.Hosting;

namespace ActualChat;

public static class ServiceCollectionExt
{
    public static BackendRoleBuilder AddBackendRole(
        this IServiceCollection services,
        HostInfo hostInfo,
        HostRole serverRole)
        => new (services, hostInfo, serverRole);

    public static IServiceCollection AddBackendRole(
        this IServiceCollection services,
        HostInfo hostInfo,
        HostRole serverRole,
        Action<BackendRoleBuilder> configure)
    {
        var builder = services.AddBackendRole(hostInfo, serverRole);
        configure.Invoke(builder);
        return services;
    }
}
