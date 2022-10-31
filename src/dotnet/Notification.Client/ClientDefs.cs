using RestEase;

namespace ActualChat.Notification.Client;

[BasePath("notifications")]
public interface INotificationsClientDef
{
    [Get(nameof(ListRecentNotificationIds))]
    Task<ImmutableArray<string>> ListRecentNotificationIds(Session session, CancellationToken cancellationToken);

    [Get(nameof(GetNotification))]
    Task<NotificationEntry> GetNotification(Session session, string notificationId, CancellationToken cancellationToken);

    [Post(nameof(RegisterDevice))]
    Task RegisterDevice([Body] INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken);

    [Post(nameof(HandleNotification))]
    public Task HandleNotification([Body] INotifications.HandleNotificationCommand command, CancellationToken cancellationToken);
}
