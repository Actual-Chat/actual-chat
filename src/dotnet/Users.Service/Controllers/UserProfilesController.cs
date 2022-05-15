using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserProfilesController : ControllerBase, IUserProfiles
{
    private readonly IUserProfiles _service;

    public UserProfilesController(IUserProfiles service)
        => _service = service;

    [HttpGet, Publish]
    public Task<UserProfile?> Get(Session session, CancellationToken cancellationToken)
        => _service.Get(session, cancellationToken);

    [HttpGet, Publish]
    public Task<UserProfile?> GetByUserId(Session session, string userId, CancellationToken cancellationToken)
        => _service.GetByUserId(session, userId, cancellationToken);

    [HttpGet, Publish]
    public Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken)
        => _service.GetUserAuthor(userId, cancellationToken);

    [HttpPost]
    public Task Update([FromBody] IUserProfiles.UpdateCommand command, CancellationToken cancellationToken)
        => _service.Update(command, cancellationToken);
}
