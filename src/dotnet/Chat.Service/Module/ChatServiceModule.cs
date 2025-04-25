using System.Net;
using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.ML;
using ActualChat.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Integrations.Anthropic;
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

        // UserLinks
        rpcHost.AddApiOrLocal<IUserLinks, UserLinks>();
        rpcHost.AddBackend<IUserLinksBackend, UserLinksBackend>();

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

        // IBackendChatMarkupHub
        services.AddSingleton(c =>
            new CachingKeyedFactory<IBackendChatMarkupHub, ChatId, BackendChatMarkupHub>(c, 4096, true).ToGeneric());

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        const string openAiChatCompletionServiceKey = "open_ai:chat_completion";
        var rateLimitedChatCompletionServiceKey = RateLimitedServiceKey.GetFor(openAiChatCompletionServiceKey);

        if (Settings.IsTranslationEnabled || Settings.IsSummarizationEnabled) {
            var httpClient = new HttpClient(new OpenAIRateLimitsLoggingHandler(new OpenAIRateLimitsLoggingHandler.Options(false)) {
                InnerHandler = new HttpClientHandler {
                    Proxy = !Settings.OpenAIProxy.IsNullOrEmpty() ? new WebProxy(Settings.OpenAIProxy) : null,
                    UseProxy = !Settings.OpenAIProxy.IsNullOrEmpty(),
                },
            }) {
                Timeout = Settings.TranslatorHttpClientTimeout,
            };
            services.AddKernel()
                .AddOpenAIChatCompletion(Settings.OpenAIChatModel,
                    Settings.OpenAIApiKey,
                    httpClient: httpClient,
                    serviceId: openAiChatCompletionServiceKey);
            services.AddKeyedSingleton(openAiChatCompletionServiceKey, httpClient); // for disposal

            services.AddKeyedSingleton<IChatCompletionService>(rateLimitedChatCompletionServiceKey,
                (svp, _) => {
                    var chatCompletion = svp.GetRequiredKeyedService<IChatCompletionService>(openAiChatCompletionServiceKey);
                    var rateLimiter = RedisTokenBucketRateLimiter.Create<ChatDbContext>(
                        new RedisTokenBucketRateLimiter.Options(
                            "rate_limit:openai_chat_completion",
                            200_000,
                            TimeSpan.FromSeconds(60)
                        ),
                        svp);
                    return chatCompletion.WrapWithRateLimiter(rateLimiter);
                });
        }

        if (Settings.IsTranslationEnabled) {
            Settings.DetectLanguagesPromptFile.RequireFileExists();
            Settings.TranslatePromptFile.RequireFileExists();
            services.AddKeyedTransient<IChatCompletionService>(
                Translator.ServiceKey,
                (svp, _) => svp.GetRequiredKeyedService<IChatCompletionService>(rateLimitedChatCompletionServiceKey));
        }
        services.AddSingleton<Translator>();
        services.AddSingleton<LanguageDetectionSerializer>();
        services.AddAnthropicServices();

        // Flows
        services.AddFlows()
            .Add<LanguageDetectionFlow>();

        // Keyed registration for ConversationSplitFlow
        services.AddKeyedSingleton<IEntryGroupExtractor>(EntryGroupLimit.None,
            (c, _) => new EntryGroupExtractor(c.GetRequiredService<IEmbeddingsCalculator>(), c.LogFor<EntryGroupExtractor>()));

        if (Settings.IsSummarizationEnabled) {
            services.AddKeyedTransient<IChatCompletionService>(
                ConversationSummarizer.ServiceKey,
                (svp, _) => svp.GetRequiredKeyedService<IChatCompletionService>(rateLimitedChatCompletionServiceKey));
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
            .Add<ChatMasterFlow>()
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
                (e, shardKey) => e.ChatId == shardKey.ChatId && e.Kind == shardKey.Kind,
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

            // DbUserLink
            db.AddEntityResolver<string, DbUserLink>();

            // DbUserLink
            db.AddEntityResolver<string, DbChatRoulette>();

            // DbConversation
            db.AddEntityResolver<string, DbConversation>();
        });
    }
}
