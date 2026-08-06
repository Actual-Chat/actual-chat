using ActualChat.Db.Module;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Flows;
using ActualChat.Redis.Module;
using ActualChat.Search;

namespace ActualChat.MLSearch.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MLSearchServiceModule(IServiceProvider moduleServices)
    : HostModule<MLSearchSettings>(moduleServices), IServerModule
{
    private readonly ILogger<MLSearchServiceModule> _log = moduleServices.LogFor<MLSearchServiceModule>();

    protected override void InjectServices(IServiceCollection services)
    {
        if (!Settings.IsEnabled) {
            _log.LogInformation("MLSearch functionality is disabled, skipping service registrations");
            return;
        }

        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<ISearchBackend>() is ServiceMode.Client;

        rpcHost.AddApi<ISearch, Search>();
        rpcHost.AddBackend<ISearchBackend, SearchBackend>();

        if (isBackendClient)
            return;

        // Shared backend services

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<MLSearchDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, MLSearchDbInitializer>();
        dbModule.AddDbContextServices<MLSearchDbContext>(services, Settings.Db, db => { });

        // OpenSearch
        services.ConfigureOpenSearch(Cfg, HostInfo, Settings);
        services.AddSingleton<OpenSearchConfigurator>()
            .AddHostedService(c => c.GetRequiredService<OpenSearchConfigurator>());

        // Indexing
        services.AddSingleton<IndexedDocuments>();

        // Flows
        services.AddFlows()
            .Add<EntryIndexingFlow>()
            .Add<EntryIndexingMasterFlow>()
            .Add<PlaceIndexingFlow>()
            .Add<GroupIndexingFlow>()
            .Add<AccountIndexingFlow>()
            .Add<PlaceContactIndexingFlow>()
            .Add<UserContactIndexingFlow>();
    }
}
