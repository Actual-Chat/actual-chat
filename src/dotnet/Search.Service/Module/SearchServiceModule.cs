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

        // Commander handlers
        rpcHost.Commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<SearchDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<SearchDbContext>))
                return true;

            // 2. Check if we're running on the client backend
            if (isBackendClient)
                return false;

            // 3. Make sure the handler is intact only for local commands
            var commandNamespace = commandType.Namespace;
            return commandNamespace.OrdinalStartsWith(typeof(ISearchBackend).Namespace!)
                || commandNamespace.OrdinalContains("Tests")
                || commandType == typeof(TextEntryChangedEvent); // Event
        });
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
        var openSearchClusterUri = Settings.ElasticLocalUri
            ?? throw new InvalidOperationException("OpenSearchClusterUri is not set");

        var connectionSettings = new ConnectionSettings(new SingleNodeConnectionPool(new Uri(openSearchClusterUri)));
        services.AddSingleton<IOpenSearchClient>(_ => new OpenSearchClient(connectionSettings));
        services.AddSingleton<OpenSearchConfigurator>()
            .AddHostedService(c => c.GetRequiredService<OpenSearchConfigurator>());
        services.AddSingleton<OpenSearchNames>();
    }
}
