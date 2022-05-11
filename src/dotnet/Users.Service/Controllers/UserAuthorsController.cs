using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserAuthorsController : ControllerBase, IUserAuthors
{
    private readonly IUserAuthors _service;

    public UserAuthorsController(IUserAuthors service) => _service = service;

    [HttpGet, Publish]
    public Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken)
        => _service.Get(userId, inherit, cancellationToken);

    [HttpGet, Publish]
    public Task<string> GetName(Session session, CancellationToken cancellationToken)
        => _service.GetName(session, cancellationToken);

    [HttpPost]
    public Task UpdateName([FromBody] IUserAuthors.UpdateNameCommand command, CancellationToken cancellationToken)
        => _service.UpdateName(command, cancellationToken);
}
