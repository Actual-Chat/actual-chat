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
    public Task<Author?> GetByUserIdAndChatId(Session session, UserId userId, ChatId chatId, CancellationToken cancellationToken)
        => _service.GetByUserIdAndChatId(session, userId, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorInfo?> GetByAuthorId(Session session, AuthorId authorId, CancellationToken cancellationToken)
        => _service.GetByAuthorId(session, authorId, cancellationToken);

}
