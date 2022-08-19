using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class UserPresencesController : ControllerBase, IUserPresences
{
    private readonly IUserPresences _service;
    private readonly ICommander _commander;

    public UserPresencesController(IUserPresences service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<Presence> Get(string userId, CancellationToken cancellationToken)
        => _service.Get(userId, cancellationToken);
}
