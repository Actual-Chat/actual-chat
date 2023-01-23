using RestEase;

namespace ActualChat.Notification;

[BasePath("notifications")]
public interface INotificationsClientDef
{
    [Get(nameof(Get))]
    Task<Notification> Get(Session session, NotificationId notificationId, CancellationToken cancellationToken);
    [Get(nameof(ListRecentNotificationIds))]
    Task<IReadOnlyList<NotificationId>> ListRecentNotificationIds(
        Session session, Moment minSentAt, CancellationToken cancellationToken);

    [Post(nameof(Handle))]
    public Task Handle([Body] INotifications.HandleCommand command, CancellationToken cancellationToken);
    [Post(nameof(RegisterDevice))]
    Task RegisterDevice([Body] INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken);
}
