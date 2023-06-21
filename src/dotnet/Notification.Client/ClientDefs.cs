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
    public Task Handle([Body] Notifications_Handle command, CancellationToken cancellationToken);
    [Post(nameof(RegisterDevice))]
    Task RegisterDevice([Body] Notifications_RegisterDevice command, CancellationToken cancellationToken);
}
