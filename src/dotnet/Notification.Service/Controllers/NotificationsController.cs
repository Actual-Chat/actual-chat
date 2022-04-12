using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Notification.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class NotificationsController : ControllerBase, INotifications
{
    private readonly INotifications _service;

    public NotificationsController(INotifications service)
        => _service = service;

    [HttpGet]
    [Publish]
    public Task<bool> IsSubscribedToChat(Session session, string chatId, CancellationToken cancellationToken)
        => _service.IsSubscribedToChat(session, chatId, cancellationToken);

    [HttpPost]
    public Task<bool> RegisterDevice(INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
        => _service.RegisterDevice(command, cancellationToken);

    [HttpPost]
    public Task<bool> SubscribeToChat(INotifications.SubscribeToChatCommand command, CancellationToken cancellationToken)
        => _service.SubscribeToChat(command, cancellationToken);

    [HttpPost]
    public Task UnsubscribeToChat(INotifications.UnsubscribeToChatCommand command, CancellationToken cancellationToken)
        => _service.UnsubscribeToChat(command, cancellationToken);
}
