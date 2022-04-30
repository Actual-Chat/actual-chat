using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserPresencesController : ControllerBase, IUserPresences
{
    private readonly IUserPresences _service;

    public UserPresencesController(IUserPresences service)
        => _service = service;

    [HttpGet, Publish]
    public Task<Presence> Get(string userId, CancellationToken cancellationToken)
        => _service.Get(userId, cancellationToken);
}
