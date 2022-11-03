using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class RolesController : ControllerBase, IRoles
{
    private IRoles Service { get; }
    private ICommander Commander { get; }

    public RolesController(IRoles service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<Role?> Get(Session session, string chatId, string roleId, CancellationToken cancellationToken)
        => Service.Get(session, chatId, roleId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Role>> List(Session session, string chatId, CancellationToken cancellationToken)
        => Service.List(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, string roleId, CancellationToken cancellationToken)
        => Service.ListAuthorIds(session, chatId, roleId, cancellationToken);

    // Commands

    [HttpPost]
    public Task<Role> Change([FromBody] IRoles.ChangeCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
