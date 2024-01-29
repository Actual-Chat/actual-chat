using ActualChat.Hosting;

namespace ActualChat;

public static class ServiceCollectionExt
{
    public static ServerRoleBuilder AddServerRole(
        this IServiceCollection services,
        HostInfo hostInfo,
        HostRole serverRole)
        => new (services, hostInfo, serverRole);

    public static IServiceCollection AddServerRole(
        this IServiceCollection services,
        HostInfo hostInfo,
        HostRole serverRole,
        Action<ServerRoleBuilder> configure)
    {
        var builder = services.AddServerRole(hostInfo, serverRole);
        configure.Invoke(builder);
        return services;
    }
}
