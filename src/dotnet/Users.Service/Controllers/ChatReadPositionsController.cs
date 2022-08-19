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
    public Task<long?> Get(Session session, string chatId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, cancellationToken);

    [HttpPost]
    public Task Set([FromBody] IChatReadPositions.SetReadPositionCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
