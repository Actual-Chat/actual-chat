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
    public Task<ChatAuthor?> GetChatAuthor(Session? session, string chatId, CancellationToken cancellationToken)
    {
        session ??= _sessionResolver.Session;
        return _service.GetChatAuthor(session, chatId, cancellationToken);
    }

    [HttpGet, Publish]
    public Task<string> GetChatPrincipalId(Session? session, string chatId, CancellationToken cancellationToken)
    {
        session ??= _sessionResolver.Session;
        return _service.GetChatPrincipalId(session, chatId, cancellationToken);
    }

    [HttpGet, Publish]
    public Task<string[]> GetChatIds(Session session, CancellationToken cancellationToken)
    {
        session ??= _sessionResolver.Session;
        return _service.GetChatIds(session, cancellationToken);
    }

    [HttpGet, Publish]
    public Task<Author?> GetAuthor(string chatId, string authorId, bool inherit, CancellationToken cancellationToken)
        => _service.GetAuthor(chatId, authorId, inherit, cancellationToken);

    // Commands

    [HttpPost]
    public Task UpdateAuthor([FromBody] IChatAuthors.UpdateAuthorCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _service.UpdateAuthor(command, cancellationToken);
    }
}
