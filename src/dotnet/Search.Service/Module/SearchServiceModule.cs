using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat.Events;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Search.Db;
using ActualChat.Redis.Module;
using ActualLab.Fusion.EntityFramework.Operations;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Search.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class SearchServiceModule(IServiceProvider moduleServices) : HostModule<SearchSettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.HostKind.IsServer())
            return; // Server-side only module

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<SearchDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, SearchDbInitializer>();
        dbModule.AddDbContextServices<SearchDbContext>(services, Settings.Db, db => {
            db.AddEntityResolver<string, DbIndexedChat>();
        });

        // Commander & Fusion
        var commander = services.AddCommander();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<SearchDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<SearchDbContext>))
                return true;

            // 2. Make sure it's intact only for local commands
            var commandAssembly = commandType.Assembly;
            return commandAssembly == typeof(ISearchBackend).Assembly // Search.Contracts assembly
                || commandType == typeof(TextEntryChangedEvent);
        });

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);

        // elastic
        services.AddSingleton(c => {
            var clientSettings = !HostInfo.IsDevelopmentInstance || Settings.IsCloudElastic
                ? new ElasticsearchClientSettings(Settings.ElasticCloudId, new Base64ApiKey(Settings.ElasticApiKey))
                : new ElasticsearchClientSettings();
            return new ElasticsearchClient(clientSettings);
        });

        // Module's own services
        var fusion = services.AddFusion();
        fusion.AddService<ISearchBackend, SearchBackend>();
        fusion.AddService<IIndexedChatsBackend, IndexedChatsBackend>();
        services.AddSingleton<ElasticConfigurator>().AddAlias<IHostedService, ElasticConfigurator>();
        services.AddSingleton<IndexingQueue>().AddHostedService(c => c.GetRequiredService<IndexingQueue>());
        services.AddSingleton<ElasticNames>();
    }
}
