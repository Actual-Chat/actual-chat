using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class UserPresencesController : ControllerBase, IUserPresences
{
    private IUserPresences Service { get; }

    public UserPresencesController(IUserPresences service)
        => Service = service;

    [HttpGet, Publish]
    public Task<Presence> Get(string userId, CancellationToken cancellationToken)
        => Service.Get(userId, cancellationToken);
}
