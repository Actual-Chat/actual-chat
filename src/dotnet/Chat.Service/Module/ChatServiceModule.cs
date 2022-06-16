using ActualChat.Chat.Db;
using ActualChat.Chat.EventHandlers;
using ActualChat.Chat.Events;
using ActualChat.Db;
using ActualChat.Db.Module;
using ActualChat.Events;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
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
        dbModule.AddDbContextServices<ChatDbContext>(services, Settings.Db);
        services.AddSingleton<IDbInitializer, ChatDbInitializer>();
        services.AddDbContextServices<ChatDbContext>(dbContext => {
            dbContext.AddEntityResolver<string, DbChat>((_, options) => {
                options.QueryTransformer = dbChats => dbChats.Include(chat => chat.Owners);
            });
            dbContext.AddEntityResolver<string, DbChatAuthor>();
            dbContext.AddEntityResolver<string, DbChatRole>();
            dbContext.AddShardLocalIdGenerator(db => db.ChatAuthors, (e, shardKey) => e.ChatId == shardKey, e => e.LocalId);
            dbContext.AddShardLocalIdGenerator(db => db.ChatRoles, (e, shardKey) => e.ChatId == shardKey, e => e.LocalId);
            dbContext.AddShardLocalIdGenerator<ChatDbContext, DbChatEntry, DbChatEntryShardRef>(
                db => db.ChatEntries,
                (e, shardKey) => e.ChatId == shardKey.ChatId && e.Type == shardKey.Type,
                e => e.Id);
        });

        var fusion = services.AddFusion();
        services.AddCommander()
            .AddHandlerFilter((handler, commandType) => {
                // 1. Check if this is DbOperationScopeProvider<AudioDbContext> handler
                if (handler is not InterfaceCommandHandler<ICommand> ich)
                    return true;
                if (ich.ServiceType != typeof(DbOperationScopeProvider<ChatDbContext>))
                    return true;

                // 2. Make sure it's intact only for local commands
                var commandAssembly = commandType.Assembly;
                return commandAssembly == typeof(ChatModule).Assembly // Chat assembly
                    || commandAssembly == typeof(IChatAuthors).Assembly // Chat.Contracts assembly
                    || commandAssembly == typeof(ChatAuthors).Assembly; // Chat.Service assembly
            });

        services.AddMvc().AddApplicationPart(GetType().Assembly);

        // Chats
        services.AddSingleton(c => {
            var chatRedisDb = c.GetRequiredService<RedisDb<ChatDbContext>>();
            return chatRedisDb.GetSequenceSet<ChatEntry>("seq." + nameof(ChatEntry));
        });
        fusion.AddComputeService<IChats, Chats>();
        fusion.AddComputeService<IChatsBackend, ChatsBackend>();

        // ChatAuthors
        services.AddSingleton(c => {
            var chatRedisDb = c.GetRequiredService<RedisDb<ChatDbContext>>();
            return chatRedisDb.GetSequenceSet<ChatAuthor>("seq." + nameof(ChatAuthor));
        });
        fusion.AddComputeService<IChatAuthors, ChatAuthors>();
        fusion.AddComputeService<IChatAuthorsBackend, ChatAuthorsBackend>();

        // ContentSaver
        services.AddResponseCaching();
        services.AddCommander().AddCommandService<IContentSaverBackend, ContentSaverBackend>();

        // Events
        services.AddSingleton<IEventPublisher, LocalEventPublisher>();
        services.AddSingleton<LocalEventHub<NewChatEntryEvent>>();
        services.AddSingleton(c => c.GetRequiredService<LocalEventHub<NewChatEntryEvent>>().Reader);
        services.AddSingleton<IEventHandler<NewChatEntryEvent>, NewChatEntryEventHandler>();
        services.AddHostedService<EventListener<NewChatEntryEvent>>();
    }
}
