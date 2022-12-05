using RestEase;

namespace ActualChat.Notification;

[BasePath("notifications")]
public interface INotificationsClientDef
{
    [Get(nameof(ListRecentNotificationIds))]
    Task<ImmutableArray<string>> ListRecentNotificationIds(Session session, CancellationToken cancellationToken);
    [Get(nameof(Get))]
    Task<Notification> Get(Session session, NotificationId notificationId, CancellationToken cancellationToken);

    [Post(nameof(Handle))]
    public Task Handle([Body] INotifications.HandleCommand command, CancellationToken cancellationToken);
    [Post(nameof(RegisterDevice))]
    Task RegisterDevice([Body] INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken);
}
