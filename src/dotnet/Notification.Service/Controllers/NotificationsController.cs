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

    [HttpGet]
    [Publish]
    public Task<NotificationEntry> GetNotification(Session session, string notificationId, CancellationToken cancellationToken)
        => _service.GetNotification(session, notificationId, cancellationToken);

    [HttpGet]
    [Publish]
    public Task<ImmutableArray<string>> ListRecentNotificationIds(Session session, CancellationToken cancellationToken)
        => _service.ListRecentNotificationIds(session, cancellationToken);

    [HttpPost]
    public Task RegisterDevice([FromBody] INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetStatus([FromBody] INotifications.SetStatusCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task HandleNotification([FromBody] INotifications.HandleNotificationCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
