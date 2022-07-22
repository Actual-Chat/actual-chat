using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Invite.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class InvitesController : ControllerBase, IInvites
{
    private readonly IInvites _service;
    private readonly ICommander _commander;

    public InvitesController(IInvites service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<ImmutableArray<Invite>> ListUserInvites(Session session, CancellationToken cancellationToken)
        => _service.ListUserInvites(session, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Invite>> ListChatInvites(Session session, string chatId, CancellationToken cancellationToken)
        => _service.ListChatInvites(session, chatId, cancellationToken);

    [HttpPost]
    public Task<Invite> Generate([FromBody] IInvites.GenerateCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task<Invite> Use([FromBody] IInvites.UseCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
