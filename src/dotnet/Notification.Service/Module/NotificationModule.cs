using ActualChat.Db.Module;
using ActualChat.Notification.Db;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Plugins;

namespace ActualChat.Notification.Module;

public class NotificationModule : HostModule<NotificationSettings>
{
    public NotificationModule(IPluginInfoProvider.Query _) : base(_)
    {
    }

    [ServiceConstructor]
    public NotificationModule(IPluginHost plugins) : base(plugins)
    {
    }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        // Redis
        var redisModule = Plugins.GetPlugins<RedisModule>().Single();
        redisModule.AddRedisDb<NotificationDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Plugins.GetPlugins<DbModule>().Single();
        dbModule.AddDbContextServices<NotificationDbContext>(services, Settings.Db);
        services.AddSingleton<IDbInitializer, NotificationDbInitializer>();

        // Fusion services
        var fusion = services.AddFusion();
        services.AddCommander().AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<NotificationDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<NotificationDbContext>))
                return true;
            // 2. Make sure it's intact only for local commands
            var commandAssembly = commandType.Assembly;
            if (commandAssembly == typeof(INotifications).Assembly) // Notification.Contracts assembly
                return true;
            return false;
        });

        fusion.AddComputeService<INotifications, Notifications>();
    }
}
