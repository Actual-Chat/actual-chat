using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class UserProfilesBackendController : ControllerBase, IUserProfilesBackend
{
    private readonly IUserProfilesBackend _service;
    private readonly ICommander _commander;

    public UserProfilesBackendController(IUserProfilesBackend service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<UserProfile?> Get(string id, CancellationToken cancellationToken)
        => _service.Get(id, cancellationToken);

    [HttpGet, Publish]
    public Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken)
        => _service.GetUserAuthor(userId, cancellationToken);

    [HttpPost]
    public Task Update([FromBody] IUserProfilesBackend.UpdateCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
