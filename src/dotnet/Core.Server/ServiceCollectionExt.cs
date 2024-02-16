using ActualChat.Hosting;
using ActualChat.Rpc;

namespace ActualChat;

public static class ServiceCollectionExt
{
    public static RpcHostBuilder AddRpcHost(
        this IServiceCollection services, HostInfo hostInfo, ILogger? log = null)
        => new(services, hostInfo, log);
}
