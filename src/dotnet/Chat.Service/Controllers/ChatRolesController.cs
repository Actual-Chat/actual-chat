using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ChatRolesController : ControllerBase, IChatRoles
{
    private readonly IChatRoles _service;
    private readonly ICommander _commander;

    public ChatRolesController(IChatRoles service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<ChatRole?> Get(Session session, string chatId, string roleId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, roleId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<ChatRole>> List(Session session, string chatId, CancellationToken cancellationToken)
        => _service.List(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, string roleId, CancellationToken cancellationToken)
        => _service.ListAuthorIds(session, chatId, roleId, cancellationToken);

    // Commands

    [HttpPost]
    public Task<ChatRole> Change([FromBody] IChatRoles.ChangeCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
