using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Notification.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class NotificationsController : ControllerBase, INotifications
{
    private readonly INotifications _service;
    private readonly ICommander _commander;

    public NotificationsController(INotifications service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet]
    [Publish]
    public Task<ChatNotificationStatus> GetStatus(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetStatus(session, chatId, cancellationToken);

    [HttpPost]
    public Task RegisterDevice([FromBody] INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetStatus([FromBody] INotifications.SetStatusCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
