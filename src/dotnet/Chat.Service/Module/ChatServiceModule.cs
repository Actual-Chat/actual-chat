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
                return commandAssembly == typeof(Chat).Assembly
                    || commandAssembly == typeof(IChatAuthorsBackend.CreateCommand).Assembly
                    || commandAssembly == typeof(ChatServiceModule).Assembly;
            });

        services.AddMvc().AddApplicationPart(GetType().Assembly);

        // ChatService
        services.AddSingleton(c => {
            var chatRedisDb = c.GetRequiredService<RedisDb<ChatDbContext>>();
            return chatRedisDb.GetSequenceSet<ChatEntry>("seq." + nameof(ChatEntry));
        });
        fusion.AddComputeService<ChatService>();
        services.AddSingleton<IChats>(c => c.GetRequiredService<ChatService>());
        services.AddSingleton<IChatsBackend>(c => c.GetRequiredService<ChatService>());

        // ChatAuthorsService
        services.AddSingleton(c => {
            var chatRedisDb = c.GetRequiredService<RedisDb<ChatDbContext>>();
            return chatRedisDb.GetSequenceSet<ChatAuthor>("seq." + nameof(ChatAuthor));
        });
        fusion.AddComputeService<ChatAuthorsService>();
        services.AddSingleton<IChatAuthors>(c => c.GetRequiredService<ChatAuthorsService>());
        services.AddSingleton<IChatAuthorsBackend>(c => c.GetRequiredService<ChatAuthorsService>());
    }
}
