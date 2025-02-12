using ActualChat.Chat.ML;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Integrations.Anthropic;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Bot;
using ActualChat.MLSearch.Db;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.MLSearch.Indexing.Initializer;
using ActualChat.Redis.Module;
using Microsoft.Extensions.Hosting;
using ActualChat.Rpc;
using ActualChat.Search;
using Microsoft.SemanticKernel;
using ActualChat.MLSearch.Bot.Services;
using ActualChat.MLSearch.Flows;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<ISearchBackend>().IsClient();

        rpcHost.AddApi<ISearch, Search>();
        rpcHost.AddApi<IMLSearch, MLSearchImpl>();
        rpcHost.AddBackend<ISearchBackend, SearchBackend>();
        rpcHost.AddBackend<IMLSearchBackend, MLSearchBackend>();
        if (!Settings.IsInitialIndexingDisabled) {
            rpcHost.AddBackend<IChatIndexInitializerTrigger, ChatIndexInitializerTrigger>();
            rpcHost.AddBackend<IChatIndexTrigger, ChatIndexTrigger>();
        }
        InjectIndexingServices(rpcHost, isBackendClient, Settings);
        InjectBotServices(rpcHost, isBackendClient);

        if (Settings.IsInitialIndexingDisabled) {
            _log.LogInformation("Initial chat indexing is disabled, skipping services registration");
        }
        else {
            _log.LogInformation("Initial chat indexing is enabled");
            if (!isBackendClient) {
                services.AddSingleton<ICursorStates<ChatIndexInitializerShard.Cursor>>(
                    static c => c.CreateInstance<CursorStates<ChatIndexInitializerShard.Cursor>>(OpenSearchNames.ChatCursor));
                services.AddSingleton<IInfiniteChatSequence, InfiniteChatSequence>();
                services.AddSingleton<IChatIndexInitializerShard, ChatIndexInitializerShard>();
                services.AddSingleton(static c => c.CreateInstance<ChatIndexInitializer>(ShardScheme.MLSearchBackend))
                    .AddAlias<IChatIndexInitializer, ChatIndexInitializer>()
                    .AddAlias<IHostedService, ChatIndexInitializer>();
            }
        }

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
        services
            .ConfigureOpenSearch(Cfg, HostInfo, Settings)
            .AddWorkerPoolDependencies();
        services.AddSingleton<OpenSearchConfigurator>()
            .AddHostedService(c => c.GetRequiredService<OpenSearchConfigurator>());

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

    private void InjectIndexingServices(RpcHostBuilder rpcHost, bool isBackendClient, MLSearchSettings settings)
    {
        var services = rpcHost.Services;
        if (isBackendClient)
            return;

        // Common indexing components

        services.AddSingleton<IChatContentUpdateLoader>(
            static c => c.CreateInstance<ChatContentUpdateLoader>(
                100 // the size of a single batch of updates to load from db
            )
        );
        services.AddSingleton<ICursorStates<ChatContentCursor>>(
            static c => c.CreateInstance<CursorStates<ChatContentCursor>>(OpenSearchNames.ChatContentCursor));

        // Chat content indexing

        // Keyed registration for ChatContentArranger4
        services.AddKeyedSingleton<IEntryGroupExtractor>(EntryGroupLimit.Medium,
            (c, _) => new EntryGroupExtractor(c.GetRequiredService<IEmbeddingsCalculator>(), (int)EntryGroupLimit.Medium));

        // Embeddings
        var embeddingSettings = Cfg.Settings<EmbeddingSettings>();
        services.TryAddSingleton(embeddingSettings);
        services.TryAddSingleton<IEmbeddingsCalculator, EmbeddingsCalculator>();

        // Common types
        services.AddSingleton<IChatContentDocumentLoader, ChatContentDocumentLoader>();
        services.AddSingleton<IChatContentMapper, ChatContentMapper>();
        services.AddSingleton(_ => new DialogFragmentAnalyzer.Options { IsDiagnosticsEnabled = true });
        services.AddSingleton<IDialogFragmentAnalyzer, DialogFragmentAnalyzer>();
        services.AddSingleton<ChatContentArranger>();
        services.AddSingleton<ChatContentArranger2>();
        services.AddSingleton<ChatContentArranger3>();
        services.AddSingleton<IDocumentEntriesLoader, DefaultDocumentEntriesLoader>();
        services.AddAlias<IChatContentArranger, ChatContentArranger>(ServiceLifetime.Scoped);
        services.AddSingleton<IChatInfoIndexer, ChatInfoIndexer>();
        services.AddSingleton<IChatContentIndexerFactory, ChatContentIndexerFactory>();
        services.AddSingleton<IChatContentArrangerSelector, ChatContentArrangerSelector>();
        services.AddSingleton<IndexedDocuments>();

        // Indexing sinks
        services.AddSingleton<ISink<ChatSlice, string>>(
            static c => c.CreateInstance<SemanticIndexSink<ChatSlice>>(OpenSearchNames.ChatContent));
        // Note: This is correct. ChatInfo  must be indexed into the same index as ChatSlice and ChatEntry for Join field to work
        services.AddSingleton<ISink<ChatInfo, string>>(
            static c => c.CreateInstance<SemanticIndexSink<ChatInfo>>(OpenSearchNames.ChatContent));

        // Workers
        services.AddSingleton<IChatContentIndexWorker>(
            static c => c.CreateInstance<ChatContentIndexWorker>(
                75,  // a number of updates between flushes
                5000 // max number of updates to process in a single run
            )
        );
        services.AddWorkerPool<IChatContentIndexWorker, MLSearch_TriggerChatIndexing, (ChatId, IndexingKind), ChatId>(
            DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
        );

        // Other
        services.AddChatMLServices();
        services.AddAnthropicServices();
    }

    private void InjectBotServices(RpcHostBuilder rpcHost, bool isBackendClient)
    {
        var services = rpcHost.Services;

        if (isBackendClient)
            return;

        // NOTE(AY): Technically tool controllers should just forward the calls to backend APIs,
        // so I assume what's below shouldn't be available on ApiHost / front-end.

        services.Configure<ChatBotConversationTriggerOptions>(e => {
            e.AllowPeerBotChat = Settings.Bot?.AllowPeerBotChat ?? false;
        });
        if (Settings.Bot is { } chatbotSettings) {
            var isBotEnabled = Settings is { IsEnabled: true, Bot.IsEnabled: true };
            if (isBotEnabled) {
                rpcHost.AddBackend<IChatBotConversationTrigger, ChatBotConversationTrigger>();

                // Add Semantic Kernel
                services.AddKernel().AddOpenAIChatCompletion(
                    apiKey: chatbotSettings.OpenAI!.ApiKey,
                    modelId: chatbotSettings.OpenAI!.ChatModel);

                // Add bot
                services.Configure<ChatbotServicesSettings>(e => {
                    e.ConversationTtl = chatbotSettings.ConversationTtl;
                });
                services.AddSingleton<IFilters, Filters>();
                services.AddSingleton<IBotConversationHandler, ChatBotConversationHandler>();
                services.AddSingleton<IChatHistoryCache, ChatHistoryCache>();
                services.AddSingleton<IUserIntentDetector, UserIntentDetector>();

                services.AddSingleton<SearchBotPluginSet>();
                services.AddSingleton<IForwardPlugin, ForwardPlugin>();
                services.AddSingleton<ISearchPlugin, SearchPlugin>();

                services.AddSingleton<IChatBotWorker>(
                    static c => c.CreateInstance<ChatBotWorker>());
                services.AddWorkerPool<IChatBotWorker, MLSearch_TriggerContinueConversationWithBot, ChatId, ChatId>(
                    DuplicateJobPolicy.Drop, shardConcurrencyLevel: 10
                );
            }
        }
    }
}
