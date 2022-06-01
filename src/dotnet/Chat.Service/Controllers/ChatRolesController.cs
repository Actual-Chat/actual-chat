using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatRolesController : ControllerBase, IChatRoles
{
    private readonly IChatRoles _service;

    public ChatRolesController(IChatRoles service)
        => _service = service;

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListOwnRoleIds(Session session, string chatId, CancellationToken cancellationToken)
        => _service.ListOwnRoleIds(session, chatId, cancellationToken);

    // Commands

    [HttpPost]
    public Task Upsert(IChatRoles.UpsertCommand command, CancellationToken cancellationToken)
        => _service.Upsert(command, cancellationToken);
}
