using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat.Events;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.SearchEngine.OpenSearch;
using ActualChat.Redis.Module;
using ActualLab.Fusion.EntityFramework.Operations;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MLSearchServiceModule(IServiceProvider moduleServices) : HostModule<MLSearchSettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.HostKind.IsServer()) {
            return; // Server-side only module
        }

        // Api Adapters
        services.AddSingleton<ILoggerSource, LoggerSource>();
        services.AddSingleton<ITracerSource, TracerSource>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<MLSearchDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, MLSearchDbInitializer>();
        dbModule.AddDbContextServices<MLSearchDbContext>(services, Settings.Db, db => {
        });

        // Commander & Fusion
        var commander = services.AddCommander();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<MLSearchDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<MLSearchDbContext>))
                return true;

            // 2. Make sure it's intact only for local commands
            var commandAssembly = commandType.Assembly;
            return commandAssembly == typeof(IMLSearchBackend).Assembly // MLSearch.Contracts assembly
                || commandType == typeof(TextEntryChangedEvent);
        });

        // Module's own services
        var fusion = services.AddFusion();
        fusion.AddService<IMLSearchBackend, MLSearchBackend>();
        services.AddSingleton<IHistoryExtractor, HistoryExtractor>();
        services.AddSingleton<IResponseBuilder, ResponseBuilder>();

        var openSearchClusterUri = Settings.OpenSearchClusterUri
            ?? throw new InvalidOperationException("OpenSearchClusterUri is not set");
        var modelGroupName = Settings.OpenSearchModelGroup
            ?? throw new InvalidOperationException("OpenSearchModelGroup is not set");
        services.AddSingleton<IOpenSearchClient>(_ =>
            new OpenSearchClient(
                new Uri(openSearchClusterUri)
            )
        );
        services.AddSingleton<OpenSearchClusterSetup>(e =>
                new OpenSearchClusterSetup(
                    e.GetRequiredService<IOpenSearchClient>(),
                    modelGroupName,
                    e.GetService<ILoggerSource>(),
                    e.GetService<ITracerSource>()
                )
            )
            .AddAlias<IModuleInitializer, OpenSearchClusterSetup>();
        services.AddKeyedSingleton<ISearchEngine, OpenSearchEngine>("OpenSearch", (e, _) =>
                new OpenSearchEngine(
                    e.GetRequiredService<IOpenSearchClient>(),
                    e.GetRequiredService<OpenSearchClusterSetup>().Result ?? throw new InvalidOperationException(
                        "Initialization script was not called."
                    ),
                    e.GetRequiredService<ILoggerSource>()
                )
        );
    }
}
