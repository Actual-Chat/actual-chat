using RestEase;

namespace ActualChat.Notification.Client;

[BasePath("notifications")]
public interface INotificationsClientDef
{
    [Get(nameof(GetStatus))]
    Task<ChatNotificationStatus> GetStatus(Session session, string chatId, CancellationToken cancellationToken);

    [Post(nameof(SetStatus))]
    Task SetStatus([Body] INotifications.SetStatusCommand command, CancellationToken cancellationToken);

    [Post(nameof(RegisterDevice))]
    Task RegisterDevice([Body] INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken);
}
