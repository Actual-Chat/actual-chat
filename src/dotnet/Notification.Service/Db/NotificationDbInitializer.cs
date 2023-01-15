using ActualChat.Db;

namespace ActualChat.Notification.Db;

public class NotificationDbInitializer : DbInitializer<NotificationDbContext>
{
    public NotificationDbInitializer(IServiceProvider services) : base(services)
    { }
}
