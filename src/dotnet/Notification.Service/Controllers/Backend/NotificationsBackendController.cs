using ActualChat.Notification.Backend;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Notification.Controllers.Backend;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class NotificationsBackendController : ControllerBase, INotificationsBackend
{
    private readonly INotificationsBackend _service;
    private readonly ICommander _commander;

    public NotificationsBackendController(INotificationsBackend service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<ImmutableArray<Device>> ListDevices(string userId, CancellationToken cancellationToken)
        => _service.ListDevices(userId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListSubscriberIds(string chatId, CancellationToken cancellationToken)
        => _service.ListSubscriberIds(chatId, cancellationToken);

    [HttpPost]
    public Task NotifySubscribers(
        INotificationsBackend.NotifySubscribersCommand subscribersCommand,
        CancellationToken cancellationToken)
        => _commander.Call(subscribersCommand, cancellationToken);

    [HttpPost]
    public Task RemoveDevices(
        INotificationsBackend.RemoveDevicesCommand removeDevicesCommand,
        CancellationToken cancellationToken)
        => _commander.Call(removeDevicesCommand, cancellationToken);
}
