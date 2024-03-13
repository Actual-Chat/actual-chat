using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.MLSearch.ApiAdapters;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        fusion.AddService<IChatIndexTrigger, ChatIndexTrigger>();

        services.AddSingleton<IDataIndexer<ChatId>, ChatHistoryExtractor>();
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
        services.AddSingleton<ICursorStates<ChatHistoryExtractor.Cursor>>(static services
            => services.CreateInstanceWith<CursorStates<ChatHistoryExtractor.Cursor>>(IndexNames.ChatSliceCursor));
        services.AddSingleton<ISink<ChatEntry, ChatEntry>>(static services
            => services.CreateInstanceWith<Sink<ChatEntry, ChatSlice>>(IndexNames.ChatSlice));
        services.AddSingleton<IDocumentMapper<ChatEntry, ChatSlice>, ChatSliceMapper>();
        services.AddSingleton<IChatIndexerWorker, ChatIndexerWorker>();

        // TODO: remove workaround. Reason: NodesByRole.TryGetValue(shardScheme.Id, out var nodes)
        Symbol ShardingSchemeId = HostRole.MLSearchIndexing;
        services.AddShardScheme(ShardingSchemeId, HostRole.MLSearchIndexing, shardCount: 12);
        services.AddKeyedSingleton<ShardWorkerFunc>(
            "OpenSearch Chat Index",
            (e, key) => new ShardWorkerFunc(
                shardingSchemeId: ShardingSchemeId,
                e,
                e.GetRequiredService<IChatIndexerWorker>().ExecuteAsync
            )
        );
        
        // -- Register ML bot --
        fusion.AddService<IChatBotConversationTrigger, ChatBotConversationTrigger>();

        services.AddKeyedSingleton<IBotConversationHandler, SampleChatBot>("Sample Chat Bot");
        services.AddKeyedSingleton(
            typeof(IDataIndexer<ChatId>), 
            "Bot chat", 
            (e, _key) => e.CreateInstanceWith<ChatHistoryExtractor>(
                e.GetRequiredKeyedService<IBotConversationHandler>("Sample Chat Bot")
            )
        );
        services.AddSingleton<IChatBotWorker>(e=>
            e.CreateInstanceWith<ChatBotWorker>(
                e.GetRequiredKeyedService<IDataIndexer<ChatId>>("Bot chat")
            )
        );
        services.AddKeyedSingleton(
            "Goo",
            (e,k) => new ShardWorkerFunc(
                shardingSchemeId: ShardingSchemeId,
                e,
                e.GetRequiredService<IChatBotWorker>().ExecuteAsync
            )
        );
        services.AddSingleton<IHostedService>(e=>e.GetRequiredKeyedService<ShardWorkerFunc>("Goo"));
    }
}