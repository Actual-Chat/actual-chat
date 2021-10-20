using ActualChat.Chat.Client;
using ActualChat.Chat.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis;
using ActualChat.Redis.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Plugins;

namespace ActualChat.Chat.Module;

public class ChatModule : HostModule<ChatSettings>
{
    public ChatModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        // Redis-related
        var redisModule = Plugins.GetPlugins<RedisModule>().Single();
        redisModule.AddRedisDb<ChatDbContext>(services, Settings.Redis);

        // DB-related
        var dbModule = Plugins.GetPlugins<DbModule>().Single();
        dbModule.AddDbContextServices<ChatDbContext>(services, Settings.Db);
        services.AddSingleton<IDbInitializer, ChatDbInitializer>();
        services.AddDbContextServices<ChatDbContext>(dbContext => {
            dbContext.AddEntityResolver<string, DbChat>((_, options) => {
                options.QueryTransformer = dbChats => dbChats.Include(chat => chat.Owners);
            });
            dbContext.AddEntityResolver<string, DbChatEntry>();
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
                if (commandAssembly == typeof(Chat).Assembly)
                    return true;

                return false;
            });

        services.AddMvc().AddApplicationPart(GetType().Assembly);
        services.AddSingleton<IMarkupParser, MarkupParser>();

        // IChatService
        services.AddSingleton(c => {
            var chatRedisDb = c.GetRequiredService<RedisDb<ChatDbContext>>();
            return chatRedisDb.GetSequenceSet<ChatService>("chat.seq");
        });
        fusion.AddComputeService<ChatService>();
        services.AddSingleton(c => (IChatService)c.GetRequiredService<ChatService>());
        services.AddSingleton(c => (IServerSideChatService)c.GetRequiredService<ChatService>());
        services.AddSingleton<IChatMediaStorageResolver, BuiltInChatMediaStorageResolver>();
    }
}
