using System.Net;
using ActualChat.AI;
using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.ML;
using ActualChat.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis;
using ActualChat.Redis.Module;
using ActualChat.Roulette;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.Module;

public sealed class ChatServiceModule(IServiceProvider moduleServices)
    : HostModule<ChatSettings>(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IChatsBackend>().IsClient();

        // Chats
        rpcHost.AddApiOrLocal<IChats, Chats>(); // Used by many
        rpcHost.AddBackend<IChatsBackend, ChatsBackend>();
        rpcHost.AddBackend<IChatsUpgradeBackend, ChatsUpgradeBackend>();
        services.AddSingleton<MediaStorage>();

        // Places
        rpcHost.AddApiOrLocal<IPlaces, Places>(); // Used by Chats
        rpcHost.AddBackend<IPlacesBackend, PlacesBackend>();

        // Authors
        rpcHost.AddApiOrLocal<IAuthors, Authors>(); // Used by Chats
        rpcHost.AddBackend<IAuthorsBackend, AuthorsBackend>();
        rpcHost.AddBackend<IAuthorsUpgradeBackend, AuthorsUpgradeBackend>();

        // Roles
        rpcHost.AddApiOrLocal<IRoles, Roles>(); // Used by Authors -> Chats
        rpcHost.AddBackend<IRolesBackend, RolesBackend>();

        // Mentions
        rpcHost.AddApi<IMentions, Mentions>();
        rpcHost.AddBackend<IMentionsBackend, MentionsBackend>();

        // Reactions
        rpcHost.AddApi<IReactions, Reactions>();
        rpcHost.AddBackend<IReactionsBackend, ReactionsBackend>();

        // Aliases
        rpcHost.AddApiOrLocal<IAliases, Aliases>();
        rpcHost.AddBackend<IAliasBackend, AliasBackend>();

        // Chat Roulette
        rpcHost.AddApiOrLocal<IRoulette, Roulette>();
        rpcHost.AddBackend<IRouletteBackend, RouletteBackend>();

        // Translation
        rpcHost.AddApiOrLocal<ITranslations, Translations>();
        rpcHost.AddBackend<ITranslationsBackend, TranslationsBackend>();
        rpcHost.AddBackend<IChatEntryLanguagesBackend, ChatEntryLanguagesBackend>();

        // Conversations
        rpcHost.AddApiOrLocal<IConversations, Conversations>();
        rpcHost.AddBackend<IConversationsBackend, ConversationsBackend>();

        // Chat threads
        rpcHost.AddApiOrLocal<IChatThreads, ChatThreads>();
        rpcHost.AddBackend<IChatThreadsBackend, ChatThreadsBackend>();

        // IBackendChatMarkupHub
        services.AddSingleton(c =>
            new CachingKeyedFactory<IBackendChatMarkupHub, ChatId, BackendChatMarkupHub>(c, 4096, true).ToGeneric());

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        if (Settings.IsTranslationEnabled) {
            Settings.LanguageDetection.PromptFile.RequireFileExists();
            Settings.Translation.PromptFile.RequireFileExists();
            AddKeyedOpenAI(services,
                Constants.Translation.ServiceKey,
                Settings.Translation.OpenAIModel,
                Settings.Translation.OpenAIKey,
                Settings.Translation.HttpTimeout);
            AddKeyedOpenAI(services,
                Constants.LanguageDetection.ServiceKey,
                Settings.LanguageDetection.OpenAIModel,
                Settings.LanguageDetection.OpenAIKey,
                Settings.LanguageDetection.HttpTimeout);
        }
        services.AddSingleton<Translator>();
        services.AddSingleton<LanguageDetector>();
        services.AddAIServices();

        // Keyed registration for ConversationSplitFlow
        services.AddKeyedSingleton<IEntryGroupExtractor>(EntryGroupLimit.None,
            (c, _) => new EntryGroupExtractor(c.GetRequiredService<IEmbeddingsCalculator>(), c.LogFor<EntryGroupExtractor>()));

        if (Settings.IsSummarizationEnabled) {
            AddKeyedOpenAI(services, ConversationSummarizer.ServiceKey, Settings.OpenAIChatModel, Settings.OpenAIApiKey);
            services.AddSingleton<IConversationSummarizer, ConversationSummarizer>();
            services.AddSingleton<IThreadInsightExtractor, ThreadInsightExtractor>();
        }
        else {
            services.AddSingleton<IConversationSummarizer, ConversationSummarizerStub>();
            services.AddSingleton<IThreadInsightExtractor, ThreadInsightExtractorStub>();
        }

        // Embeddings
        var embeddingSettings = Cfg.Settings<EmbeddingSettings>();
        services.TryAddSingleton(embeddingSettings);
        services.TryAddSingleton<IEmbeddingsCalculator, EmbeddingsCalculator>();

        // Flows
        services.AddFlows()
            .Add<ConversationSplitMasterFlow>()
            .Add<ConversationSplitFlow>();

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
            db.AddShardLocalIdGenerator<ChatDbContext, DbChatEntry, DbChatEntryShardRef>(
                dbContext => dbContext.ChatEntries,
                (e, shardKey) => e.ChatId == shardKey.ChatId.Value && e.Kind == shardKey.Kind,
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

            // DbChatRoulette
            db.AddEntityResolver<string, DbChatRoulette>();

            // DbConversation
            db.AddEntityResolver<string, DbConversation>();
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
            openAIKey = Settings.OpenAIApiKey;
        if (openAIModel.IsNullOrEmpty())
            openAIModel = Settings.OpenAIChatModel;

        // unlimited
        var unlimitedServiceKey = serviceKey + "_Unlimited";
        services.AddKernel().AddOpenAIChatCompletion(openAIModel, openAIKey, serviceId: unlimitedServiceKey, httpClient: httpClient);

        // rate-limited
        var rateLimitedKey = serviceKey + "_RateLimited";
        services.AddKeyedSingleton<IChatCompletionService>(rateLimitedKey,
            (c, _) => {
                var chatCompletion = c.GetRequiredKeyedService<IChatCompletionService>(unlimitedServiceKey);
                var rateLimiter = RedisTokenBucketRateLimiter.Create<ChatDbContext>(
                    new RedisTokenBucketRateLimiter.Options(
                        $"rate_limit:openai:{serviceKey}",
                        200_000,
                        TimeSpan.FromSeconds(60)
                    ),
                    c);
                return chatCompletion.WrapWithRateLimiter(rateLimiter);
            });

        // for serviceKey
        services.AddKeyedSingleton<IChatCompletionService>(serviceKey,
            (c, _) => c.GetRequiredKeyedService<IChatCompletionService>(rateLimitedKey));
    }
}
