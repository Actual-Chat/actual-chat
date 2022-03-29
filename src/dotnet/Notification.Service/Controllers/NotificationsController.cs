using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Notification.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class NotificationsController : ControllerBase, INotifications
{
    [HttpPost]
    public virtual async Task RegisterDevice(INotifications.RegisterDeviceCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    [HttpPost]
    public virtual async Task SubscribeToChat(INotifications.SubscribeToChatCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
