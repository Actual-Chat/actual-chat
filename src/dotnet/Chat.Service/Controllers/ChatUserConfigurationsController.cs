using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatUserConfigurationsController : ControllerBase, IChatUserConfigurations
{
    private readonly IChatUserConfigurations _service;
    private readonly ISessionResolver _sessionResolver;

    public ChatUserConfigurationsController(IChatUserConfigurations service, ISessionResolver sessionResolver)
    {
        _service = service;
        _sessionResolver = sessionResolver;
    }

    [HttpGet, Publish]
    public Task<string> GetTranscriptionLanguage(Session? session, string chatId, CancellationToken cancellationToken)
    {
        session ??= _sessionResolver.Session;
        return _service.GetTranscriptionLanguage(session, chatId, cancellationToken);
    }

    // Commands

    [HttpPost]
    public Task<Unit> SetTranscriptionLanguage([FromBody] IChatUserConfigurations.SetTranscriptionLanguageCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _service.SetTranscriptionLanguage(command, cancellationToken);
    }
}
