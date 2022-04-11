using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatReadPositionsController: ControllerBase, IChatReadPositions
{
    private readonly IChatReadPositions _service;
    private readonly ISessionResolver _sessionResolver;

    public ChatReadPositionsController(IChatReadPositions service, ISessionResolver sessionResolver)
    {
        _service = service;
        _sessionResolver = sessionResolver;
    }

    [HttpGet, Publish]
    public Task<long?> GetReadPosition(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetReadPosition(session, chatId, cancellationToken);

    [HttpPost]
    public Task UpdateReadPosition(IChatReadPositions.UpdateReadPositionCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _service.UpdateReadPosition(command, cancellationToken);
    }
}
