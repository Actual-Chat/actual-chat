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
    public Task<Notification?> Get(Session session, NotificationId notificationId, CancellationToken cancellationToken)
        => Service.Get(session, notificationId, cancellationToken);

    [HttpGet, Publish]
    public Task<IReadOnlyList<NotificationId>> ListRecentNotificationIds(
        Session session, Moment minVersion, CancellationToken cancellationToken)
        => Service.ListRecentNotificationIds(session, minVersion, cancellationToken);

    [HttpPost]
    public Task RegisterDevice([FromBody] INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task Handle([FromBody] INotifications.HandleCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
