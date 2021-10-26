using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class AuthorController : ControllerBase, IAuthorServiceFacade
{
    private readonly IAuthorServiceFacade _service;

    public AuthorController(IAuthorServiceFacade service)
        => _service = service;

    [HttpGet, Publish]
    public Task<Author> GetByUserId(Session session, UserId userId, CancellationToken cancellationToken)
        => _service.GetByUserId(session, userId, cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorInfo> GetByAuthorId(Session session, AuthorId authorId, CancellationToken cancellationToken)
        => _service.GetByAuthorId(session, authorId, cancellationToken);
}
