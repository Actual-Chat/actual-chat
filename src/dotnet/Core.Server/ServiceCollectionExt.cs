using ActualChat.Hosting;

namespace ActualChat;

public static class ServiceCollectionExt
{
    public static BackendServiceBuilder AddBackend(this IServiceCollection services, HostInfo hostInfo)
        => new(services, hostInfo);
}
