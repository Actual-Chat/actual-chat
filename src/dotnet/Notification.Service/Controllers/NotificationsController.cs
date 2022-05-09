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
    public Task<ChatNotificationStatus> GetStatus(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetStatus(session, chatId, cancellationToken);

    [HttpPost]
    public Task RegisterDevice(INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
        => _service.RegisterDevice(command, cancellationToken);

    [HttpPost]
    public Task SetStatus(INotifications.SetStatusCommand command, CancellationToken cancellationToken)
        => _service.SetStatus(command, cancellationToken);
}
