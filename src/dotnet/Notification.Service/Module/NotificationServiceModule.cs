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
public sealed class NotificationServiceModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IServerModule
{
    private static readonly object FirebaseAppFactoryLock = new();

    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<INotificationsBackend>().IsClient();

        // Notifications
        rpcHost.AddApi<INotifications, Notifications>();
        rpcHost.AddBackend<INotificationsBackend, NotificationsBackend>();

        // NOTE(AY): Notifications service uses NotificationDbContext and FirebaseMessaging,
        // so we have to register them in any case.

        // Firebase
        services.AddSingleton(_ => {
            lock (FirebaseAppFactoryLock) {
                var firebaseApp = FirebaseApp.DefaultInstance ?? FirebaseApp.Create();
                return FirebaseMessaging.GetMessaging(firebaseApp);
            }
        });
        services.AddSingleton<FirebaseMessagingClient>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<NotificationDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, NotificationDbInitializer>();
        dbModule.AddDbContextServices<NotificationDbContext>(services,
            db => db.AddEntityResolver<string, DbNotification>());
    }
}
