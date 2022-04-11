using ActualChat.Notification.Backend;
using RestEase;

namespace ActualChat.Notification.Client;

[BasePath("notificationsBackend")]
public interface INotificationsBackendClientDef
{
    [Get(nameof(GetDevices))]
    public Task<Device[]> GetDevices(string userId, CancellationToken cancellationToken);

    [Get(nameof(GetSubscribers))]
    public Task<string[]> GetSubscribers(string chatId, CancellationToken cancellationToken);

    [Post(nameof(NotifySubscribers))]
    public Task NotifySubscribers(
        [Body]INotificationsBackend.NotifySubscribersCommand subscribersCommand,
        CancellationToken cancellationToken);
}
