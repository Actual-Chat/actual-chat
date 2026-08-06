using ActualChat.Db.Module;
using ActualChat.Flows.Db;
using ActualChat.Flows.Infrastructure;
using ActualChat.Redis.Module;

namespace ActualChat.Flows.Module;

public sealed class FlowsServiceModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IFlowBackend>() is ServiceMode.Client;

        // Flows
        rpcHost.AddBackend<IFlowBackend, FlowBackend>();
        services.AddSingleton(c => new FlowHub(c));
        services.AddSingleton(c => new FlowDefs(c));
        services.AddFlows();

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddSingleton<MasterFlowStarter>()
            .AddHostedService(c => c.GetRequiredService<MasterFlowStarter>());

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
