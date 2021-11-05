using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

// users here to fix AmbiguousMatchException (bc chat service has the same controller name)
[Route("api/users/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserAuthorsBackendController : ControllerBase, IUserAuthorsBackend
{
    private readonly IUserAuthorsBackend _service;

    public UserAuthorsBackendController(IUserAuthorsBackend service) => _service = service;

    [HttpGet, Publish]
    public Task<UserAuthor?> Get(UserId userId, bool inherit, CancellationToken cancellationToken)
        => _service.Get(userId, inherit, cancellationToken);
}
