using System.Diagnostics.CodeAnalysis;
using ActualChat.Db.Module;
using ActualChat.Notification.Db;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Notification.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class NotificationServiceModule(IServiceProvider moduleServices) : HostModule(moduleServices)
{
    private static readonly object FirebaseAppFactoryLock = new();

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.HostKind.IsServer())
            return; // Server-side only module

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<NotificationDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, NotificationDbInitializer>();
        dbModule.AddDbContextServices<NotificationDbContext>(services,
            db => db.AddEntityResolver<string, DbNotification>());

        // Commander & Fusion
        var commander = services.AddCommander();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<NotificationDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<NotificationDbContext>))
                return true;

            // 2. Make sure it's intact only for local commands
            var commandNamespace = commandType.Namespace;
            return commandNamespace.OrdinalStartsWith(typeof(INotifications).Namespace!);
        });
        var fusion = services.AddFusion();

        // Module's own services
        fusion.AddService<INotifications, Notifications>();
        fusion.AddService<INotificationsBackend, NotificationsBackend>();

        // Firebase
        services.AddSingleton(_ => {
            lock (FirebaseAppFactoryLock) {
                var firebaseApp = FirebaseApp.DefaultInstance ?? FirebaseApp.Create();
                return FirebaseMessaging.GetMessaging(firebaseApp);
            }
        });
        services.AddSingleton<FirebaseMessagingClient>();

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
