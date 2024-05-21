using System.Diagnostics.CodeAnalysis;
using ActualChat.Db.Module;
using ActualChat.Flows.Db;
using ActualChat.Flows.Infrastructure;
using ActualChat.Hosting;
using ActualChat.Redis.Module;

namespace ActualChat.Flows.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class FlowsServiceModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IFlows>().IsClient();

        // Flows
        rpcHost.AddBackend<IFlows, DbFlows>();
        services.AddSingleton(c => new FlowRegistry(c));
        services.AddSingleton(c => new FlowEventForwarder(c));
        rpcHost.Commander.AddHandlers<FlowEventForwarder>();
        services.AddFlows();

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddSingleton(c => new FlowHost(c));

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<FlowsDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, FlowsDbInitializer>();
        dbModule.AddDbContextServices<FlowsDbContext>(services, db => {
            // Overriding / adding extra DbAuthentication services
            db.AddEntityResolver<string, DbFlow>();
        });
    }
}
