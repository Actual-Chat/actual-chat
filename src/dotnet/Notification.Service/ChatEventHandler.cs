using ActualChat.Chat.Events;

namespace ActualChat.Notification;

public class ChatEventHandler<T>: IChatEventHandler<T> where T: IChatEvent
{
    private readonly IChatEventNotificationGenerator<T> _notificationGenerator;
    private readonly INotificationPublisher _publisher;

    public ChatEventHandler(IChatEventNotificationGenerator<T> notificationGenerator, INotificationPublisher publisher)
    {
        _notificationGenerator = notificationGenerator;
        _publisher = publisher;
    }

    public virtual async Task Notify(IChatEventHandler<T>.NotifyCommand command, CancellationToken cancellationToken)
    {
        var chatEvent = command.Event;
        var notifications = _notificationGenerator.GenerateNotifications(chatEvent, cancellationToken).ConfigureAwait(false);
        await foreach (var notification in notifications.ConfigureAwait(false))
            await _publisher.Publish(notification, cancellationToken).ConfigureAwait(false);
    }
}
