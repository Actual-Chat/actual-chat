using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserStatesController : ControllerBase, IUserStates
{
    private readonly IUserStates _service;

    public UserStatesController(IUserStates service)
        => _service = service;

    [HttpGet, Publish]
    public Task<bool> IsOnline(string userId, CancellationToken cancellationToken)
        => _service.IsOnline(userId, cancellationToken);
}
