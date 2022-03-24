using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Notification.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class NotificationsController : ControllerBase, INotifications
{
    // [HttpPost]
    // public Task<NotificationEntry> Create(INotifications.CreateCommand command, CancellationToken cancellationToken)
    //     => throw new NotImplementedException();
}
