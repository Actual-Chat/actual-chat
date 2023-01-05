using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat.Db;
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

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
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
                QueryTransformer = query => query.Include(a => a.Roles),
            });

            // DbRole
            db.AddShardLocalIdGenerator(dbContext => dbContext.Roles,
                (e, shardKey) => e.ChatId == shardKey, e => e.LocalId);
            db.AddEntityResolver<string, DbRole>();
        });

        // Commander & Fusion
        var commander = services.AddCommander();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<AudioDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<ChatDbContext>))
                return true;

            // 2. Make sure it's intact only for local commands
            var commandAssembly = commandType.Assembly;
            return commandAssembly == typeof(ChatModule).Assembly // Chat assembly
                || commandAssembly == typeof(IAuthors).Assembly // Chat.Contracts assembly
                || commandAssembly == typeof(Authors).Assembly // Chat.Service assembly
                || commandType == typeof(NewUserEvent); // NewUserEvent is handled by Chat service - TODO(AK): abstraction leak!!
        });
        var fusion = services.AddFusion();
        fusion.AddLocalCommandScheduler(Queues.Chats);
        commander.AddEventHandlers();

        // Chats
        fusion.AddComputeService<IChats, Chats>();
        fusion.AddComputeService<IChatsBackend, ChatsBackend>();
        commander.AddCommandService<IChatsUpgradeBackend, ChatsUpgradeBackend>();

        // Authors
        fusion.AddComputeService<IAuthors, Authors>();
        fusion.AddComputeService<IAuthorsBackend, AuthorsBackend>();
        commander.AddCommandService<IAuthorsUpgradeBackend, AuthorsUpgradeBackend>();

        // Roles
        fusion.AddComputeService<IRoles, Roles>();
        fusion.AddComputeService<IRolesBackend, RolesBackend>();

        // Mentions
        fusion.AddComputeService<IMentions, Mentions>();
        fusion.AddComputeService<IMentionsBackend, MentionsBackend>();

        // Reactions
        fusion.AddComputeService<IReactions, Reactions>();
        fusion.AddComputeService<IReactionsBackend, ReactionsBackend>();

        // ContentSaver
        services.AddResponseCaching();
        commander.AddCommandService<IContentSaverBackend, ContentSaverBackend>();

        // ChatMarkupHub
        services.AddSingleton(c =>
            new CachingKeyedFactory<IBackendChatMarkupHub, ChatId, BackendChatMarkupHub>(c, 4096, true).ToGeneric());

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
