using System.Diagnostics.CodeAnalysis;
using ActualChat.Db.Module;
using ActualChat.Notification.Db;
using ActualChat.Hosting;
using ActualChat.Notification.Backend;
using ActualChat.Redis.Module;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Notification.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class NotificationServiceModule(IServiceProvider moduleServices)
    : HostModule<NotificationSettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<NotificationDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, NotificationDbInitializer>();
        dbModule.AddDbContextServices<NotificationDbContext>(services, Settings.Db,
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
            var commandAssembly = commandType.Assembly;
            if (commandAssembly == typeof(INotifications).Assembly) // Notification.Contracts assembly
                return true;
            return false;
        });
        var fusion = services.AddFusion();

        // Module's own services
        fusion.AddService<INotifications, Notifications>();
        fusion.AddService<INotificationsBackend, NotificationsBackend>();

        // Firebase
        var firebaseApp = FirebaseApp.DefaultInstance ?? FirebaseApp.Create();
        var firebaseMessaging = FirebaseMessaging.GetMessaging(firebaseApp);
        services.AddSingleton(firebaseMessaging);
        services.AddSingleton<FirebaseMessagingClient>();

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
