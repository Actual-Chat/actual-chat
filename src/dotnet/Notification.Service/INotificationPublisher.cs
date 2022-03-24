namespace ActualChat.Notification;

public interface INotificationPublisher
{
    Task Publish(NotificationEntry notification, CancellationToken cancellationToken);
}
