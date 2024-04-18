using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Bot;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.Indexing;
using ActualChat.MLSearch.Engine.OpenSearch;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.MLSearch.Indexing;
using ActualChat.Redis.Module;
using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.Extensions.Hosting;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MLSearchServiceModule(IServiceProvider moduleServices) : HostModule<MLSearchSettings>(moduleServices)
{
    private readonly ILogger<MLSearchServiceModule> _log = moduleServices.LogFor<MLSearchServiceModule>();

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.HostKind.IsServer()) {
            return; // Server-side only module
        }
        if (!Settings.IsEnabled) {
            _log.LogInformation("MLSearch functionality is disabled, skipping service registrations.");
            return;
        }

        // Api Adapters
        services.AddSingleton<ITracerSource, TracerSource>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<MLSearchDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, MLSearchDbInitializer>();
        dbModule.AddDbContextServices<MLSearchDbContext>(services, Settings.Db, db => {
        });

        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);

        // Module's own services

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
        services.AddSingleton<ISearchEngine<ChatSlice>>(static services
            => services.CreateInstanceWith<OpenSearchEngine<ChatSlice>>(IndexNames.ChatSlice));

        services.AddWorkerPoolDependencies();

        // -- Register chat indexer --
        const string IndexServiceGroup = "OpenSearch Chat Index";
        rpcHost.AddBackend<IChatIndexTrigger, ChatIndexTrigger>();

        services.AddSingleton<IDocumentMapper<ChatEntry, ChatSlice>, ChatSliceMapper>();
        services.AddSingleton<ICursorStates<ChatHistoryExtractor.Cursor>>(static services
            => services.CreateInstanceWith<CursorStates<ChatHistoryExtractor.Cursor>>(IndexNames.ChatSliceCursor));
        services.AddSingleton<ISink<ChatEntry, ChatEntry>>(static services
            => services.CreateInstanceWith<Sink<ChatEntry, ChatSlice>>(IndexNames.ChatSlice));

        services.AddKeyedSingleton<IDataIndexer<ChatId>, ChatHistoryExtractor>(IndexServiceGroup);
        services.AddSingleton<IChatIndexerWorker>(static services
            => services.CreateInstanceWith<ChatIndexerWorker>(
                15, // max iteration count before rescheduling
                services.GetRequiredKeyedService<IDataIndexer<ChatId>>(IndexServiceGroup)
            )
        );
        services.AddWorkerPool<IChatIndexerWorker, MLSearch_TriggerChatIndexing, ChatId, ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );

        if (Settings.IsInitialIndexingDisabled) {
            _log.LogInformation("Initial chat indexing is disabled, skipping services registration.");
        }
        else {
            // -- Register chat index initializer --
            rpcHost.AddBackend<IChatIndexInitializerTrigger, ChatIndexInitializerTrigger>();
            services.AddSingleton<ICursorStates<ChatIndexInitializerShard.Cursor>>(static services
                => services.CreateInstanceWith<CursorStates<ChatIndexInitializerShard.Cursor>>(IndexNames.ChatCursor));
            services.AddSingleton<IChatIndexInitializerShard, ChatIndexInitializerShard>();
            services.AddSingleton(static services
                => services.CreateInstanceWith<ChatIndexInitializer>(
                    ShardScheme.MLSearchBackend))
                .AddAlias<IChatIndexInitializer, ChatIndexInitializer>()
                .AddAlias<IHostedService, ChatIndexInitializer>();
        }

        // -- Register ML bot --
        const string ConversationBotServiceGroup = "ML Chat Bot";
        rpcHost.AddBackend<IChatBotConversationTrigger, ChatBotConversationTrigger>();

        services.AddKeyedSingleton<IBotConversationHandler, SampleChatBot>(ConversationBotServiceGroup);
        services.AddKeyedSingleton<IDataIndexer<ChatId>>(
            ConversationBotServiceGroup,
            static (services, serviceKey) => services.CreateInstanceWith<ChatHistoryExtractor>(
                services.GetRequiredKeyedService<IBotConversationHandler>(serviceKey)
            )
        );
        services.AddSingleton<IChatBotWorker>(static services
            => services.CreateInstanceWith<ChatBotWorker>(
                services.GetRequiredKeyedService<IDataIndexer<ChatId>>(ConversationBotServiceGroup)
            )
        );
        services.AddWorkerPool<IChatBotWorker, MLSearch_TriggerContinueConversationWithBot, ChatId, ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );
    }
}
