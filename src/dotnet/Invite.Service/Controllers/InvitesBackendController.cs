using ActualChat.Invite.Backend;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Invite.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class InvitesBackendController : ControllerBase, IInvitesBackend
{
    private readonly IInvitesBackend _service;
    private readonly ICommander _commander;

    public InvitesBackendController(IInvitesBackend service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<ImmutableArray<Invite>> GetAll(string searchKey, int minRemaining, CancellationToken cancellationToken)
        => _service.GetAll(searchKey, minRemaining, cancellationToken);

    [HttpGet, Publish]
    public Task<Invite?> Get(string id, CancellationToken cancellationToken)
        => _service.Get(id, cancellationToken);

    [HttpPost]
    public Task<Invite> Generate(IInvitesBackend.GenerateCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task<Invite> Use(IInvitesBackend.UseCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
