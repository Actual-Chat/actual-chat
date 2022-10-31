using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ReadPositionsController: ControllerBase, IReadPositions
{
    private readonly IReadPositions _service;
    private readonly ICommander _commander;

    public ReadPositionsController(IReadPositions service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<long?> Get(Session session, string chatId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, cancellationToken);

    [HttpPost]
    public Task Set([FromBody] IReadPositions.SetCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
