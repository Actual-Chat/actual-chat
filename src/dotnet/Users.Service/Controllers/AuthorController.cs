using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

// users here to fix AmbiguousMatchException (bc chat service has the same controller name)
[Route("api/users/[controller]/[action]")]
[ApiController, JsonifyErrors]
[Internal]
public class AuthorController : ControllerBase, IDefaultAuthorService
{
    private readonly IDefaultAuthorService _service;

    public AuthorController(IDefaultAuthorService service) => _service = service;

    [HttpGet, Publish]
    public Task<IAuthorInfo?> Get(UserId userId, CancellationToken cancellationToken)
        => _service.Get(userId, cancellationToken);
}
