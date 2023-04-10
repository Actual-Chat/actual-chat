using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public sealed class ChatPositionsController: ControllerBase, IChatPositions
{
    private IChatPositions Service { get; }
    private ICommander Commander { get; }

    public ChatPositionsController(IChatPositions service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<ChatPosition> GetOwn(Session session, ChatId chatId, ChatPositionKind kind, CancellationToken cancellationToken)
        => Service.GetOwn(session, chatId, kind, cancellationToken);

    [HttpPost]
    public Task Set([FromBody] IChatPositions.SetCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
