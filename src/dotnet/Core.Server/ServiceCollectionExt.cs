using ActualChat.Hosting;
using ActualChat.Rpc;

namespace ActualChat;

public static class ServiceCollectionExt
{
    // HasService

    public static bool HasService<TService>(this IServiceCollection services, object serviceKey)
        => services.HasService(typeof(TService), serviceKey);
    public static bool HasService(this IServiceCollection services, Type serviceType, object serviceKey)
        => services.Any(d => d.ServiceType == serviceType && ReferenceEquals(d.ServiceKey, serviceKey));

    public static RpcHostBuilder AddRpcHost(
        this IServiceCollection services, HostInfo hostInfo, ILogger? log = null)
        => new(services, hostInfo, log);
}
