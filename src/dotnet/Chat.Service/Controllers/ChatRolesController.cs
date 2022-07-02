using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
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
    public Task<ImmutableArray<Symbol>> ListOwnRoleIds(Session session, string chatId, CancellationToken cancellationToken)
        => _service.ListOwnRoleIds(session, chatId, cancellationToken);

    // Commands

    [HttpPost]
    public Task Upsert(IChatRoles.UpsertCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
