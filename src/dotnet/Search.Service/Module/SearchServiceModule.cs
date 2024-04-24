using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat.Events;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Search.Db;
using ActualChat.Redis.Module;
using ActualLab.Fusion.EntityFramework.Operations;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.Search.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class SearchServiceModule(IServiceProvider moduleServices)
    : HostModule<SearchSettings>(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<ISearchBackend>().IsClient();

        // Search
        rpcHost.AddBackend<ISearchBackend, SearchBackend>();

        // Indexing
        rpcHost.AddBackend<IIndexedChatsBackend, IndexedChatsBackend>();
        rpcHost.AddBackend<IContactIndexStatesBackend, ContactIndexStateBackend>();

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddSingleton<TextEntryIndexer>()
            .AddHostedService(c => c.GetRequiredService<TextEntryIndexer>());
        services.AddSingleton<UserContactIndexer>()
            .AddHostedService(c => c.GetRequiredService<UserContactIndexer>());
        services.AddSingleton<ChatContactIndexer>()
            .AddHostedService(c => c.GetRequiredService<ChatContactIndexer>());

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<SearchDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, SearchDbInitializer>();
        dbModule.AddDbContextServices<SearchDbContext>(services, db => {
            db.AddEntityResolver<string, DbIndexedChat>();
            db.AddEntityResolver<string, DbContactIndexState>();
        });

        // OpenSearch
        var openSearchClusterUri = Settings.LocalUri
            ?? throw new InvalidOperationException("OpenSearchClusterUri is not set");

        var connectionSettings = new ConnectionSettings(
            new SingleNodeConnectionPool(new Uri(openSearchClusterUri)),
            sourceSerializer: (builtin, settings) => new OpenSearchJsonSerializer(builtin, settings));
        services.AddSingleton<IOpenSearchClient>(_ => new OpenSearchClient(connectionSettings));
        services.AddSingleton(s => ActivatorUtilities.CreateInstance<OpenSearchConfigurator>(s, HostInfo.IsDevelopmentInstance))
            .AddHostedService(c => c.GetRequiredService<OpenSearchConfigurator>());
        services.AddSingleton<IndexNames>();
    }
}
