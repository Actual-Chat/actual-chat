using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.Indexing;
using ActualChat.MLSearch.Engine.Indexing.Spout;
using ActualChat.MLSearch.Engine.OpenSearch;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.Redis.Module;
using ActualLab.Fusion.EntityFramework.Operations;
using OpenSearch.Client;
using OpenSearch.Net;

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

        var connectionSettings = new ConnectionSettings(
            new SingleNodeConnectionPool(new Uri(openSearchClusterUri)),
            sourceSerializer: (builtin, settings) => new OpenSearchJsonSerializer(builtin, settings));

        services.AddSingleton<IOpenSearchClient>(_ => new OpenSearchClient(connectionSettings));
        services.AddSingleton(e => ActivatorUtilities.CreateInstance<ClusterSetup>(e, modelGroupName))
            .AddAlias<IModuleInitializer, ClusterSetup>();

        services.AddSingleton<IIndexSettingsSource, IndexSettingsSource>();
        // ChatSlice engine registrations
        services.AddSingleton<ISearchEngine<ChatSlice>>(services
            => services.CreateInstanceWith<OpenSearchEngine<ChatSlice>>(IndexNames.ChatSlice));
        services.AddSingleton<ICursorStates<ChatEntriesIndexer.Cursor>>(services
            => services.CreateInstanceWith<CursorStates<ChatEntriesIndexer.Cursor>>(IndexNames.ChatSliceCursor));
        services.AddSingleton<ISink<ChatEntry, ChatEntry>>(services
            => services.CreateInstanceWith<Sink<ChatEntry, ChatSlice>>(IndexNames.ChatSlice));
        services.AddSingleton<IDocumentMapper<ChatEntry, ChatSlice>, ChatSliceMapper>();
        services.AddSingleton<ChatEntriesIndexer>();

        // TODO: remove workaround. Reason: NodesByRole.TryGetValue(shardScheme.Id, out var nodes)
        Symbol ShardingSchemeId = HostRole.MLSearchIndexing;
        services.AddShardScheme(ShardingSchemeId, HostRole.MLSearchIndexing, shardCount: 12);
        services.AddKeyedSingleton<ShardWorkerFunc>(
            "OpenSearch Chat Index",
            (e, key) => new ShardWorkerFunc(
                shardingSchemeId: ShardingSchemeId,
                e,
                e.GetRequiredService<ChatEntriesIndexer>().Execute
            )
        );
        services.AddHostedService(e => e
            .GetRequiredKeyedService<ShardWorkerFunc>("OpenSearch Chat Index")
        );

        // TODO: remove once events are settled:
        // -- start of TODO item --
        fusion.AddService<IChatEntriesSpout, ChatEntriesSpout>();
        // Note: this singleton must work in the main app backend.
        fusion.AddService<ChatEntriesEventsDispatcher>();
        // -- end of TODO item --
    }
}
