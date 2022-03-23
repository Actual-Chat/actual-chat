using ActualChat.Notification.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public class Notifications : DbServiceBase<NotificationDbContext>, INotifications
{
    private readonly IAuth _auth;

    public Notifications(IServiceProvider services, IAuth auth) : base(services)
        => _auth = auth;

}
