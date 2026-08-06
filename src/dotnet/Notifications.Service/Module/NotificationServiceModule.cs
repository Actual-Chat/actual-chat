using ActualChat.Db.Module;
using ActualChat.Notifications.Db;
using ActualChat.Redis.Module;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;

namespace ActualChat.Notifications.Module;

public sealed class NotificationServiceModule(IServiceProvider moduleServices)
    : HostModule<NotificationsSettings>(moduleServices), IServerModule
{
    private static readonly Lock FirebaseAppFactoryLock = new();

    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<INotificationsBackend>() is ServiceMode.Client;

        // Notifications
        rpcHost.AddApi<INotifications, NotificationsService>();
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
        services.AddSingleton<IFirebaseMessagingClient, FirebaseMessagingClient>();

        // Direct APNs - Push to Talk wakes only (FCM can't deliver pushtotalk pushes)
        services.AddSingleton<IApnsClient, ApnsClient>();
        services.AddHttpClient(ApnsClient.HttpClientName)
            .ConfigureHttpClient((c, httpClient) => {
                var s = c.GetRequiredService<NotificationsSettings>();
                httpClient.BaseAddress = new Uri(s.ApplePushUseSandbox
                    ? "https://api.sandbox.push.apple.com"
                    : "https://api.push.apple.com");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler {
                EnableMultipleHttp2Connections = true,
            });

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<NotificationDbContext>(services);

        // Flows
        services.AddFlows().Add<Flows.MentionReminderFlow>();

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, NotificationDbInitializer>();
        dbModule.AddDbContextServices<NotificationDbContext>(services,
            db => {
                db.AddEntityResolver<string, DbExplicitNotification>();
            });

    }
}
