using ActualChat.Db;

namespace ActualChat.Notifications.Db;

public class NotificationDbInitializer(IServiceProvider services) : DbInitializer<NotificationDbContext>(services);
