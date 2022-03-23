using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Notification.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class NotificationsController : ControllerBase, INotifications
{
}
