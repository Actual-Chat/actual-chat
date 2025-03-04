using System.Net;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using ActualChat.Roulette;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;

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

        // Chat Roulette
        rpcHost.AddApiOrLocal<ITranslations, Translations>();
        rpcHost.AddBackend<ITranslationsBackend, TranslationsBackend>();

        // IBackendChatMarkupHub
        services.AddSingleton(c =>
            new CachingKeyedFactory<IBackendChatMarkupHub, ChatId, BackendChatMarkupHub>(c, 4096, true).ToGeneric());

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        if (Settings.IsTranslationEnabled)
            services.AddKernel()
                .AddOpenAIChatCompletion(Settings.OpenAIChatModel,
                    Settings.OpenAIApiKey,
                    httpClient: new HttpClient(new HttpClientHandler {
                        Proxy = !Settings.OpenAIProxy.IsNullOrEmpty() ? new WebProxy(Settings.OpenAIProxy) : null,
                        UseProxy = !Settings.OpenAIProxy.IsNullOrEmpty(),
                    }),
                    serviceId: Translator.ServiceKey);
        services.AddSingleton<Translator>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<ChatDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, ChatDbInitializer>();
        dbModule.AddDbContextServices<ChatDbContext>(services, db => {
            // DbChat
            db.AddEntityResolver<string, DbChat>();

            // Translation
            db.AddEntityResolver<string, DbTranslation>();

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
        });
    }
}
