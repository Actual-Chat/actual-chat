using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class RolesController : ControllerBase, IRoles
{
    private readonly IRoles _service;
    private readonly ICommander _commander;

    public RolesController(IRoles service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<Role?> Get(Session session, string chatId, string roleId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, roleId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Role>> List(Session session, string chatId, CancellationToken cancellationToken)
        => _service.List(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, string roleId, CancellationToken cancellationToken)
        => _service.ListAuthorIds(session, chatId, roleId, cancellationToken);

    // Commands

    [HttpPost]
    public Task<Role> Change([FromBody] IRoles.ChangeCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
