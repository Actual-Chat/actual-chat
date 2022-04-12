using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatUserSettingsController : ControllerBase, IChatUserSettings
{
    private readonly IChatUserSettings _service;

    public ChatUserSettingsController(IChatUserSettings service)
        => _service = service;

    [HttpGet, Publish]
    public Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, cancellationToken);

    // Commands

    [HttpPost]
    public Task Set([FromBody] IChatUserSettings.SetCommand command, CancellationToken cancellationToken)
        => _service.Set(command, cancellationToken);
}
