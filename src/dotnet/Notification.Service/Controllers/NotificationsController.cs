using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Notification.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class NotificationsController : ControllerBase, INotifications
{
    private INotifications Service { get; }
    private ICommander Commander { get; }

    public NotificationsController(INotifications service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<NotificationEntry> GetNotification(Session session, Symbol notificationId, CancellationToken cancellationToken)
        => Service.GetNotification(session, notificationId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListRecentNotificationIds(Session session, CancellationToken cancellationToken)
        => Service.ListRecentNotificationIds(session, cancellationToken);

    [HttpPost]
    public Task RegisterDevice([FromBody] INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task HandleNotification([FromBody] INotifications.HandleNotificationCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
