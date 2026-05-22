namespace ActualChat.Notifications;

public interface IFirebaseMessagingClient
{
    Task SendMessage(
        Notification notification,
        IReadOnlyCollection<Symbol> deviceIds,
        bool? enableDataCollection,
        int badgeCount,
        CancellationToken cancellationToken);

    Task SendDismissal(
        IReadOnlyCollection<NotificationId> dismissedIds,
        IReadOnlyCollection<Symbol> deviceIds,
        int badgeCount,
        CancellationToken cancellationToken);
}
