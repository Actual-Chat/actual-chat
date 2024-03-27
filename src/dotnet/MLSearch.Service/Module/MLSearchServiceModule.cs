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
    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.HostKind.IsServer()) {
            return; // Server-side only module
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
        services.AddSingleton<ISearchEngine<ChatSlice>>(static services
            => services.CreateInstanceWith<OpenSearchEngine<ChatSlice>>(IndexNames.ChatSlice));

        services.AddWorkerPoolDependencies();

        // -- Register chat indexer --
        const string IndexServiceGroup = "OpenSearch Chat Index";
        fusion.AddService<IChatIndexTrigger, ChatIndexTrigger>();

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

        // -- Register chat index initializer --
        fusion.AddService<IChatIndexInitializerTrigger, ChatIndexInitializerTrigger>();
        services.AddSingleton<ICursorStates<ChatIndexInitializerShard.Cursor>>(static services
            => services.CreateInstanceWith<CursorStates<ChatIndexInitializerShard.Cursor>>(IndexNames.ChatCursor));
        services.AddSingleton<IChatIndexInitializerShard, ChatIndexInitializerShard>();
        services.AddSingleton(static services
            => services.CreateInstanceWith<ChatIndexInitializer>(
                ShardScheme.MLSearchBackend))
            .AddAlias<IChatIndexInitializer, ChatIndexInitializer>()
            .AddAlias<IHostedService, ChatIndexInitializer>();

        // -- Register ML bot --
        const string ConversationBotServiceGroup = "ML Chat Bot";
        fusion.AddService<IChatBotConversationTrigger, ChatBotConversationTrigger>();

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
