using ActualChat.Notifications.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[CollectionDefinition(nameof(NotificationCollection))]
public class NotificationCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("notification", messageSink, TestAppHostOptions.Default with {
        ConfigureHost = (__, cfg) => {
            // Shrunk so WalkieTalkiePushTest can prove wake-dedup expiry without a real 30s wait.
            _ = cfg.AddInMemory<NotificationsSettings>((x => x.WalkieTalkieWakeTtl, "0:0:2"));
        },
    });
