using ActualChat.Db;

namespace ActualChat.Notification.Db;

public class NotificationDbInitializer(IServiceProvider services) : DbInitializer<NotificationDbContext>(services);
