using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatUserSettingsController : ControllerBase, IChatUserSettings
{
    private readonly IChatUserSettings _service;
    private readonly ISessionResolver _sessionResolver;

    public ChatUserSettingsController(IChatUserSettings service, ISessionResolver sessionResolver)
    {
        _service = service;
        _sessionResolver = sessionResolver;
    }

    [HttpGet, Publish]
    public Task<ChatUserSettings?> Get(Session? session, string chatId, CancellationToken cancellationToken)
    {
        session ??= _sessionResolver.Session;
        return _service.Get(session, chatId, cancellationToken);
    }

    // Commands

    [HttpPost]
    public Task<Unit> Set([FromBody] IChatUserSettings.SetCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _service.Set(command, cancellationToken);
    }
}
