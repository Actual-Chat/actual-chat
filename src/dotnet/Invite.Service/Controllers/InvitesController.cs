using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Invite.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class InvitesController : ControllerBase, IInvites
{
    private readonly IInvites _service;

    public InvitesController(IInvites service)
        => _service = service;

    [HttpGet, Publish]
    public Task<IImmutableList<Invite>> GetUserInvites(Session session, CancellationToken cancellationToken)
        => _service.GetUserInvites(session, cancellationToken);

    [HttpGet, Publish]
    public Task<IImmutableList<Invite>> GetChatInvites(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetChatInvites(session, chatId, cancellationToken);

    [HttpPost]
    public Task<Invite> Generate(IInvites.GenerateCommand command, CancellationToken cancellationToken)
        => _service.Generate(command, cancellationToken);

    [HttpPost]
    public Task<Invite> Use(IInvites.UseCommand command, CancellationToken cancellationToken)
        => _service.Use(command, cancellationToken);
}
