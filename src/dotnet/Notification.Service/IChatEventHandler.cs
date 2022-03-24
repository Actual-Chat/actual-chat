using ActualChat.Chat.Events;

namespace ActualChat.Notification;

public interface IChatEventHandler<in T> where T : IChatEvent
{
    Task<IReadOnlyCollection<NotificationEntry>> GenerateNotifications(
        T chatEvent,
        CancellationToken cancellationToken);
}
