using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public class Notifications : DbServiceBase<NotificationDbContext>, INotifications, INotificationsBackend
{
    private readonly IAuth _auth;

    public Notifications(IServiceProvider services, IAuth auth) : base(services)
        => _auth = auth;


    // [ComputeMethod]
    public Task<Device[]> GetDevices(string userId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    // [CommandHandler]
    public virtual async Task RegisterDevice(INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    // [CommandHandler]
    public virtual async Task SubscribeToChat(INotifications.SubscribeToChatCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
