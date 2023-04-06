using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class UserPresencesController : ControllerBase, IUserPresences
{
    private IUserPresences Service { get; }
    private ICommander Commander { get; }

    public UserPresencesController(IUserPresences service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
        => Service.Get(userId, cancellationToken);

    [HttpPost]
    public Task CheckIn(IUserPresences.CheckInCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
