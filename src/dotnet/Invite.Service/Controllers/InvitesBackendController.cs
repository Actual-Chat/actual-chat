using ActualChat.Invite.Backend;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Invite.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class InvitesBackendController : ControllerBase, IInvitesBackend
{
    private readonly IInvitesBackend _service;

    public InvitesBackendController(IInvitesBackend service)
        => _service = service;

    [HttpGet, Publish]
    public Task<IImmutableList<Invite>> GetAll(CancellationToken cancellationToken)
        => _service.GetAll(cancellationToken);

    [HttpGet, Publish]
    public Task<Invite?> GetByCode(string inviteCode, CancellationToken cancellationToken)
        => _service.GetByCode(inviteCode, cancellationToken);

    [HttpPost]
    public Task<Invite> Generate(IInvitesBackend.GenerateCommand command, CancellationToken cancellationToken)
        => _service.Generate(command, cancellationToken);

    [HttpPost]
    public Task UseInvite(IInvitesBackend.UseInviteCommand command, CancellationToken cancellationToken)
        => _service.UseInvite(command, cancellationToken);
}
