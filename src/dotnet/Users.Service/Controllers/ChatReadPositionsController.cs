using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ChatReadPositionsController: ControllerBase, IChatReadPositions
{
    private readonly IChatReadPositions _service;
    private readonly ICommander _commander;

    public ChatReadPositionsController(IChatReadPositions service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<long?> GetReadPosition(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetReadPosition(session, chatId, cancellationToken);

    [HttpPost]
    public Task UpdateReadPosition([FromBody] IChatReadPositions.UpdateReadPositionCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
