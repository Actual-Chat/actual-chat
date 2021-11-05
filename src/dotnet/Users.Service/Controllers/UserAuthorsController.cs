using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

// users here to fix AmbiguousMatchException (bc chat service has the same controller name)
[Route("api/users/[controller]/[action]")]
[ApiController, JsonifyErrors]
[Internal]
public class UserAuthorsController : ControllerBase, IUserAuthors
{
    private readonly IUserAuthors _service;

    public UserAuthorsController(IUserAuthors service) => _service = service;

    [HttpGet, Publish]
    public Task<UserAuthor?> Get(UserId userId, CancellationToken cancellationToken)
        => _service.Get(userId, cancellationToken);
}
