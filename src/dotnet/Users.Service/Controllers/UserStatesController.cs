using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserStatesController : ControllerBase, IUserStates
{
    private readonly IUserStates _userStates;
    private readonly ISessionResolver _sessionResolver;

    public UserStatesController(IUserStates userStates, ISessionResolver sessionResolver)
    {
        _userStates = userStates;
        _sessionResolver = sessionResolver;
    }

    [HttpGet, Publish]
    public Task<bool> IsOnline(UserId userId, CancellationToken cancellationToken)
        => _userStates.IsOnline(userId, cancellationToken);
}
