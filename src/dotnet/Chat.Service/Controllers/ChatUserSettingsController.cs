using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

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
    public Task<LanguageId> GetLanguage(Session? session, string chatId, CancellationToken cancellationToken)
    {
        session ??= _sessionResolver.Session;
        return _service.GetLanguage(session, chatId, cancellationToken);
    }

    // Commands

    [HttpPost]
    public Task<Unit> SetLanguage([FromBody] IChatUserSettings.SetLanguageCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _service.SetLanguage(command, cancellationToken);
    }
}
