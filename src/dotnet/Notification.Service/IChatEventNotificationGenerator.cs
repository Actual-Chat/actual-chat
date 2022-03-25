using ActualChat.Chat.Events;

namespace ActualChat.Notification;

public interface IChatEventNotificationGenerator<in T> where T : IChatEvent
{
    IAsyncEnumerable<NotificationEntry> GenerateNotifications(
        T chatEvent,
        CancellationToken cancellationToken);
}
