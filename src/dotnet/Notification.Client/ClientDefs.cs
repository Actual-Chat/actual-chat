using RestEase;

namespace ActualChat.Notification.Client;

[BasePath("notifications")]
public interface INotificationsClientDef
{
    [Get(nameof(IsSubscribedToChat))]
    Task<bool> IsSubscribedToChat(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(RegisterDevice))]
    Task RegisterDevice([Body] INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken);

    [Post(nameof(SubscribeToChat))]
    Task SubscribeToChat([Body] INotifications.SubscribeToChatCommand command, CancellationToken cancellationToken);

    [Post(nameof(UnsubscribeToChat))]
    Task UnsubscribeToChat([Body] INotifications.UnsubscribeToChatCommand command, CancellationToken cancellationToken);
}
