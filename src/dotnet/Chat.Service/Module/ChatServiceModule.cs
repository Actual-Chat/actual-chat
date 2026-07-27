using System.Net;
using ActualChat.AI;
using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.ML;
using ActualChat.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Redis;
using ActualChat.Redis.Module;
using ActualChat.Resilience;
using ActualChat.Streaming;
using ActualLab.Redis;
using Google.Api.Gax;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;

namespace ActualChat.Chat.Module;

public sealed class ChatServiceModule(IServiceProvider moduleServices)
    : HostModule<ChatSettings>(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IChatsBackend>() is ServiceMode.Client;

        // Chats
        rpcHost.AddLocalApi<IChats, Chats>(); // Used by many
        rpcHost.AddBackend<IChatsBackend, ChatsBackend>();
        rpcHost.AddBackend<IChatsUpgradeBackend, ChatsUpgradeBackend>();
        rpcHost.AddApi<ILegacyChats, LegacyChats>(); // v2.7 wire-format compat

        // Places
        rpcHost.AddLocalApi<IPlaces, Places>(); // Used by Chats
        rpcHost.AddBackend<IPlacesBackend, PlacesBackend>();

        // Authors
        rpcHost.AddLocalApi<IAuthors, Authors>(); // Used by Chats
        rpcHost.AddBackend<IAuthorsBackend, AuthorsBackend>();
        rpcHost.AddBackend<IAuthorsUpgradeBackend, AuthorsUpgradeBackend>();

        // Roles
        rpcHost.AddLocalApi<IRoles, Roles>(); // Used by Authors -> Chats
        rpcHost.AddBackend<IRolesBackend, RolesBackend>();

        // Mentions
        rpcHost.AddApi<IMentions, Mentions>();
        rpcHost.AddBackend<IMentionsBackend, MentionsBackend>();

        // Reactions
        rpcHost.AddApi<IReactions, Reactions>();
        rpcHost.AddBackend<IReactionsBackend, ReactionsBackend>();

        // Shared locations
        rpcHost.AddApi<ISharedLocations, SharedLocations>();
        rpcHost.AddBackend<ISharedLocationsBackend, SharedLocationsBackend>();

        // Aliases
        rpcHost.AddLocalApi<IAliases, Aliases>();
        rpcHost.AddBackend<IAliasBackend, AliasBackend>();

        // Translation
        rpcHost.AddLocalApi<ITranslations, Translations>();
        rpcHost.AddBackend<ITranslationsBackend, TranslationsBackend>();
        rpcHost.AddBackend<IChatEntryLanguagesBackend, ChatEntryLanguagesBackend>();

        // Conversations
        rpcHost.AddLocalApi<IConversations, Conversations>();
        rpcHost.AddBackend<IConversationsBackend, ConversationsBackend>();

        // Chat threads
        rpcHost.AddLocalApi<IChatThreads, ChatThreads>();
        rpcHost.AddBackend<IChatThreadsBackend, ChatThreadsBackend>();

        // IBackendChatMarkupHub
        services.AddSingleton(c =>
            new CachingKeyedFactory<IBackendChatMarkupHub, ChatId, BackendChatMarkupHub>(c, 4096, true).ToGeneric());

        // Content meta
        rpcHost.AddBackend<IContentLinksBackend, ContentLinksBackend>();
        services.AddSingleton<OpenGraphTagsProvider>();

        // Diagnostics
        rpcHost.AddLocalApi<IDiagnostics, Diagnostics>();
        rpcHost.AddBackend<IDiagnosticsBackend, DiagnosticsBackend>();
        services.AddFusion().AddComputeService<DiagnosticsBackendLocal>();
        if (HostInfo.HasRole(HostRole.Api)) {
            services.AddSingleton(c => new ShardRoutingMonitor(c));
            services.AddHostedService(c => c.GetRequiredService<ShardRoutingMonitor>());
        }

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        if (Settings.IsTranslationEnabled) {
            AddKeyedOpenAI(services,
                Constants.Translation.ServiceKey,
                Settings.Translation.OpenAIModel,
                Settings.Translation.OpenAIKey,
                Settings.Translation.HttpTimeout);
            if (!Settings.Translation.RealtimeOpenAIModel.IsNullOrEmpty() && Settings.Translation.RealtimeGeminiModel.IsNullOrEmpty())
                AddKeyedOpenAI(services,
                    Constants.Translation.RealtimeServiceKey,
                    Settings.Translation.RealtimeOpenAIModel,
                    Settings.Translation.OpenAIKey,
                    Settings.Translation.HttpTimeout);
            else if (!Settings.Translation.RealtimeGeminiModel.IsNullOrEmpty())
                AddKeyedVertexAI(services,
                    Constants.Translation.RealtimeServiceKey,
                    Settings.Translation.RealtimeGeminiModel,
                    Settings.Translation.HttpTimeout);
            if (!Settings.UseFakeLanguageDetection)
                AddKeyedOpenAI(services,
                    Constants.LanguageDetection.ServiceKey,
                    Settings.LanguageDetection.OpenAIModel,
                    Settings.LanguageDetection.OpenAIKey,
                    Settings.LanguageDetection.HttpTimeout);
        }
        services.AddSingleton<Translator>();
        services.AddKeyedSingleton<Translator>(Constants.Translation.RealtimeServiceKey);
        services.AddSingleton<LanguageDetector>();
        services.AddAIServices();
        services.AddChatMLServices();

        // Keyed registration for ConversationSplitFlow
        services.AddKeyedSingleton<IEntryGroupExtractor>(EntryGroupLimit.None,
            (c, _) => new EntryGroupExtractor(c.GetRequiredService<IEmbeddingsCalculator>(), c.LogFor<EntryGroupExtractor>()));

        if (Settings.IsSummarizationEnabled) {
            AddKeyedOpenAI(services, ConversationSummarizer.ServiceKey, Settings.Summarization.OpenAIModel, Settings.Summarization.OpenAIKey, Settings.Summarization.HttpTimeout);
            services.AddSingleton<IConversationSummarizer>(
                c => new ConversationSummarizer(
                    new ConversationSummarizer.Options {
                        PromptFile = c.GetRequiredService<CoreServerSettings>().PromptsDir | Settings.Summarization.SummarizeConversationPromptFile,
                    },
                    c));
            services.AddSingleton<IThreadInsightExtractor, ThreadInsightExtractor>(
                c => new ThreadInsightExtractor(
                    new ThreadInsightExtractor.Options {
                        PromptFile = c.GetRequiredService<CoreServerSettings>().PromptsDir | Settings.Summarization.SuggestChatThreadTitlePromptFile,
                    },
                    c));
            services.AddSingleton<IChatDigestSummarizer, ChatDigestSummarizer>(
                c => new ChatDigestSummarizer(
                    new ChatDigestSummarizer.Options {
                        PromptFile = c.GetRequiredService<CoreServerSettings>().PromptsDir | Settings.Summarization.SummarizeChatDigestPromptFile,
                    },
                    c));
        }
        else {
            services.AddSingleton<IConversationSummarizer, ConversationSummarizerStub>();
            services.AddSingleton<IThreadInsightExtractor, ThreadInsightExtractorStub>();
            services.AddSingleton<IChatDigestSummarizer, ChatDigestSummarizerStub>();
        }

        if (Settings.IsRetranscriptionEnabled) {
            services.AddSingleton(new OpenAITranscriber.Options {
                ApiKey = Settings.Retranscription.OpenAIKey.NullIfEmpty() ?? Settings.OpenAIKey,
                Model = Settings.Retranscription.OpenAIModel,
            });
            services.AddSingleton<IRefineTranscriber>(c => new OpenAITranscriber(c.GetRequiredService<OpenAITranscriber.Options>(), c));
        }

        // Embeddings
        var embeddingSettings = Cfg.Settings<EmbeddingSettings>();
        services.TryAddSingleton(embeddingSettings);
        services.TryAddSingleton<IEmbeddingsCalculator, EmbeddingsCalculator>();

        // Flows
        var flows = services.AddFlows()
            .Add<ChatEntryMigrationFlow>()
            .Add<ChatEntryMigrationFixupFlow>()
            .Add<ChatEntryEndsAtFixupFlow>()
            .Add<ChatEntryFixupFlow>()
            .Add<ConversationSplitMasterFlow>()
            .Add<ConversationSplitFlow>()
            .Add<LiveConversationSummaryFlow>()
            .Add<TranslationCleanupFlow>();
        if (Settings.IsChatContentItemIndexingEnabled)
            flows
                .Add<ChatContentIndexingMasterFlow>()
                .Add<ChatEntryContentIndexingFlow>()
                .Add<ChatMediaIndexingFlow>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<ChatDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, ChatDbInitializer>();
        dbModule.AddDbContextServices<ChatDbContext>(services, db => {
            // DbChat
            db.AddEntityResolver<string, DbChat>();

            // DbChatEntry
            db.AddShardLocalIdGenerator<ChatDbContext, DbChatEntry, string>(
                dbContext => dbContext.ChatEntries,
                (e, shardKey) => e.ChatId == shardKey && e.Kind == 0,
                e => e.LocalId);

            // DbAuthor
            db.AddShardLocalIdGenerator(dbContext => dbContext.Authors,
                (e, shardKey) => e.ChatId == shardKey, e => e.LocalId);
            db.AddEntityResolver<string, DbAuthor>(_ => new() {
                QueryTransformer = query => query
                    .Include(a => a.Roles)
                    .AsSplitQuery(),
            });

            // Translation
            db.AddEntityResolver<string, DbTranslation>();

            // DbChatEntryLanguage
            db.AddEntityResolver<string, DbChatEntryLanguage>();

            // DbRole
            db.AddShardLocalIdGenerator(dbContext => dbContext.Roles,
                (e, shardKey) => e.ChatId == shardKey, e => e.LocalId);
            db.AddEntityResolver<string, DbRole>();

            // DbCopiedChat
            db.AddEntityResolver<string, DbChatCopyState>();

            // DbPlace
            db.AddEntityResolver<string, DbPlace>();

            // DbReadPositionsStat
            db.AddEntityResolver<string, DbReadPositionsStat>();

            // DbAlias
            db.AddEntityResolver<string, DbAlias>();

            // DbConversation
            db.AddEntityResolver<string, DbConversation>();

            // DbSharedLocation
            db.AddEntityResolver<string, DbSharedLocation>();
        });
    }

    private void AddKeyedOpenAI(IServiceCollection services, string serviceKey, string openAIModel, string openAIKey, TimeSpan? httpClientTimeout = null)
    {
        var httpClient = new HttpClient(new OpenAIRateLimitsLoggingHandler(new OpenAIRateLimitsLoggingHandler.Options(false)) {
            InnerHandler = new HttpClientHandler {
                Proxy = !Settings.OpenAIProxy.IsNullOrEmpty() ? new WebProxy(Settings.OpenAIProxy) : null,
                UseProxy = !Settings.OpenAIProxy.IsNullOrEmpty(),
            },
        }) {
            Timeout = httpClientTimeout ?? TimeSpan.FromSeconds(100),
        };
        services.AddKeyedSingleton(serviceKey, httpClient); // for disposal
        if (openAIKey.IsNullOrEmpty())
            openAIKey = Settings.OpenAIKey;
        if (openAIModel.IsNullOrEmpty())
            openAIModel = Settings.OpenAIModel;

        // unlimited
        var unlimitedServiceKey = serviceKey + "_Unlimited";
        services.AddKernel().AddOpenAIChatCompletion(openAIModel, openAIKey, serviceId: unlimitedServiceKey, httpClient: httpClient);

        // rate-limited
        var rateLimitedKey = serviceKey + "_RateLimited";
        services.AddKeyedSingleton<IChatCompletionService>(rateLimitedKey,
            (c, _) => {
                var chatCompletion = c.GetRequiredKeyedService<IChatCompletionService>(unlimitedServiceKey);
                var rateLimiter = new RedisTokenBucketRateLimiter(
                    c.GetRequiredService<RedisDb<ChatDbContext>>(),
                    "",
                    new TokenBucketBudget(2_000_000, TimeSpan.FromSeconds(60)));
                return chatCompletion.WrapWithRateLimiter(rateLimiter, $"rate_limit:openai:{serviceKey}");
            });

        // for serviceKey
        services.AddKeyedSingleton<IChatCompletionService>(serviceKey,
            (c, _) => c.GetRequiredKeyedService<IChatCompletionService>(rateLimitedKey));
    }

    private void AddKeyedVertexAI(IServiceCollection services, string serviceKey, string model, TimeSpan? httpClientTimeout = null)
    {
        var httpClient = new HttpClient(new HttpClientHandler {
            Proxy = !Settings.OpenAIProxy.IsNullOrEmpty() ? new WebProxy(Settings.OpenAIProxy) : null,
            UseProxy = !Settings.OpenAIProxy.IsNullOrEmpty(),
        });
        services.AddKeyedSingleton(serviceKey, httpClient); // for disposal

        var coreSettings = Cfg.Settings<CoreServerSettings>(nameof(CoreSettings));
        if (coreSettings.GoogleProjectId.IsNullOrEmpty()) {
            var platform = Platform.Instance();
            coreSettings.GoogleProjectId = platform?.ProjectId ?? throw StandardError.NotSupported<ChatServiceModule>(
                $"Requires GKE or explicit settings of {nameof(CoreServerSettings)}.{nameof(CoreServerSettings.GoogleProjectId)}");
        }
        if (model.IsNullOrEmpty())
            model = "gemini-2.5-flash";

        // unlimited
        var unlimitedServiceKey = serviceKey + "_Unlimited";
        var hostLifetime = ModuleServices.HostLifetime(); // TODO(AY): ModuleServices shouldn't contain IHostApplicationLifetime!
        services.AddKernel()
            .AddVertexAIGeminiChatCompletion(model,
                () => GetBearerToken(hostLifetime.StopToken()),
                coreSettings.GoogleRegionId,
                coreSettings.GoogleProjectId,
                VertexAIVersion.V1_Beta,
                unlimitedServiceKey,
                httpClient);

        // rate-limited
        var rateLimitedKey = serviceKey + "_RateLimited";
        services.AddKeyedSingleton<IChatCompletionService>(rateLimitedKey,
            (c, _) => {
                var chatCompletion = c.GetRequiredKeyedService<IChatCompletionService>(unlimitedServiceKey);
                var rateLimiter = new RedisTokenBucketRateLimiter(
                    c.GetRequiredService<RedisDb<ChatDbContext>>(),
                    "",
                    new TokenBucketBudget(2_000_000, TimeSpan.FromSeconds(60)));
                return chatCompletion.WrapWithRateLimiter(rateLimiter, $"rate_limit:gemini:{serviceKey}");
            });

        // for serviceKey
        services.AddKeyedSingleton<IChatCompletionService>(serviceKey,
            (c, _) => c.GetRequiredKeyedService<IChatCompletionService>(rateLimitedKey));
    }

    private static async ValueTask<string> GetBearerToken(CancellationToken cancellationToken)
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync(cancellationToken).ConfigureAwait(false);
        // Specify the scope for Vertex AI
        credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        // Get the access token
        var token = await credential.UnderlyingCredential
            .GetAccessTokenForRequestAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return token;
    }
}
