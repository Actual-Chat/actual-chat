using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ReadPositionsController: ControllerBase, IReadPositions
{
    private IReadPositions Service { get; }
    private ICommander Commander { get; }

    public ReadPositionsController(IReadPositions service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<long?> GetOwn(Session session, ChatId chatId, CancellationToken cancellationToken)
        => Service.GetOwn(session, chatId, cancellationToken);

    [HttpPost]
    public Task Set([FromBody] IReadPositions.SetCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
