using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatAuthorsController : ControllerBase, IChatAuthors
{
    private readonly IChatAuthors _service;
    private readonly ISessionResolver _sessionResolver;

    public ChatAuthorsController(IChatAuthors service, ISessionResolver sessionResolver)
    {
        _service = service;
        _sessionResolver = sessionResolver;
    }

    [HttpGet, Publish]
    public Task<ChatAuthor?> GetSessionChatAuthor(Session? session, string chatId, CancellationToken cancellationToken)
    {
        session ??= _sessionResolver.Session;
        return _service.GetSessionChatAuthor(session, chatId, cancellationToken);
    }

    [HttpGet, Publish]
    public Task<string> GetSessionChatPrincipalId(Session? session, string chatId, CancellationToken cancellationToken)
    {
        session ??= _sessionResolver.Session;
        return _service.GetSessionChatPrincipalId(session, chatId, cancellationToken);
    }

    [HttpGet, Publish]
    public Task<Author?> GetAuthor(string chatId, string authorId, bool inherit, CancellationToken cancellationToken)
        => _service.GetAuthor(chatId, authorId, inherit, cancellationToken);
}
