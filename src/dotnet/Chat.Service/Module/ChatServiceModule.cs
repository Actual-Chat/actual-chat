using ActualChat.Chat.Db;
using ActualChat.Chat.Internal;
using ActualChat.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using ActualChat.Commands;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Plugins;
using Stl.Redis;

namespace ActualChat.Chat.Module;

public class ChatServiceModule : HostModule<ChatSettings>
{
    public ChatServiceModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatServiceModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        // Redis
        var redisModule = Plugins.GetPlugins<RedisModule>().Single();
        redisModule.AddRedisDb<ChatDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Plugins.GetPlugins<DbModule>().Single();
        services.AddSingleton<IDbInitializer, ChatDbInitializer>();
        dbModule.AddDbContextServices<ChatDbContext>(services, Settings.Db, db => {
            // DbChat
            db.AddEntityResolver<string, DbChat>(_ => new() {
                QueryTransformer = query => query.Include(chat => chat.Owners),
            });

            // DbChatEntry
            db.AddShardLocalIdGenerator<ChatDbContext, DbChatEntry, DbChatEntryShardRef>(
                dbContext => dbContext.ChatEntries,
                (e, shardKey) => e.ChatId == shardKey.ChatId && e.Type == shardKey.Type,
                e => e.Id);

            // DbChatAuthor
            db.AddShardLocalIdGenerator(dbContext => dbContext.ChatAuthors,
                (e, shardKey) => e.ChatId == shardKey, e => e.LocalId);
            db.AddEntityResolver<string, DbChatAuthor>(_ => new() {
                QueryTransformer = query => query.Include(a => a.Roles),
            });

            // DbChatRole
            db.AddShardLocalIdGenerator(dbContext => dbContext.ChatRoles,
                (e, shardKey) => e.ChatId == shardKey, e => e.LocalId);
            db.AddEntityResolver<string, DbChatRole>();
        });

        // Commander & Fusion
        var commander = services.AddCommander()
            .AddLocalEventHandlers();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<AudioDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<ChatDbContext>))
                return true;

            // 2. Make sure it's intact only for local commands
            var commandAssembly = commandType.Assembly;
            return commandAssembly == typeof(ChatModule).Assembly // Chat assembly
                || commandAssembly == typeof(IChatAuthors).Assembly // Chat.Contracts assembly
                || commandAssembly == typeof(ChatAuthors).Assembly // Chat.Service assembly
                || commandType == typeof(NewUserEvent); // NewUserEvent is handled by Chat service - TODO(AK): abstraction leak!!
        });
        var fusion = services.AddFusion();

        // Chats
        fusion.AddComputeService<IChats, Chats>();
        fusion.AddComputeService<IChatsBackend, ChatsBackend>();

        // ChatAuthors
        fusion.AddComputeService<IChatAuthors, ChatAuthors>();
        fusion.AddComputeService<IChatAuthorsBackend, ChatAuthorsBackend>();

        // ChatRoles
        fusion.AddComputeService<IChatRoles, ChatRoles>();
        fusion.AddComputeService<IChatRolesBackend, ChatRolesBackend>();

        // Mentions
        fusion.AddComputeService<IMentions, Mentions>();
        fusion.AddComputeService<IMentionsBackend, MentionsBackend>();

        // Reactions
        fusion.AddComputeService<IReactions, Reactions>();
        fusion.AddComputeService<IReactionsBackend, ReactionsBackend>();

        // ChatMentionResolver
        services.AddSingleton<BackendChatMentionResolverFactory>();

        // ContentSaver
        services.AddResponseCaching();
        commander.AddCommandService<IContentSaverBackend, ContentSaverBackend>();

        // Controllers, etc.
        var mvc = services.AddMvcCore(options => {
            options.ModelBinderProviders.Add(new ChatPrincipalIdModelBinderProvider());
        });
        mvc.AddApplicationPart(GetType().Assembly);
    }
}
