using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class AuthorController : ControllerBase, IAuthorServiceFrontend
{
    private readonly IAuthorServiceFrontend _service;

    public AuthorController(IAuthorServiceFrontend service)
        => _service = service;

    [HttpGet, Publish]
    public Task<AuthorInfo?> GetByAuthorId(Session session, AuthorId authorId, CancellationToken cancellationToken)
        => _service.GetByAuthorId(session, authorId, cancellationToken);
}
