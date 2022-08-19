using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ChatUserSettingsController : ControllerBase, IChatUserSettings
{
    private readonly IChatUserSettings _service;
    private readonly ICommander _commander;

    public ChatUserSettingsController(IChatUserSettings service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<ChatUserSettings?> Get(Session session, string chatId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, cancellationToken);

    // Commands

    [HttpPost]
    public Task Set([FromBody] IChatUserSettings.SetCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
