using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatAuthorsController : ControllerBase, IChatAuthors
{
    private readonly IChatAuthors _service;

    public ChatAuthorsController(IChatAuthors service)
        => _service = service;

    [HttpGet, Publish]
    public Task<ChatAuthor?> GetChatAuthor(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetChatAuthor(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<string> GetChatPrincipalId(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetChatPrincipalId(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<string[]> GetChatIds(Session session, CancellationToken cancellationToken)
        => _service.GetChatIds(session, cancellationToken);

    [HttpGet, Publish]
    public Task<string?> GetChatAuthorAvatarId(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetChatAuthorAvatarId(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<bool> CanAddToContacts(Session session, string chatAuthorId, CancellationToken cancellationToken)
        => _service.CanAddToContacts(session, chatAuthorId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<string>> GetAuthorIds(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetAuthorIds(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<string>> GetUserIds(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetUserIds(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<Author?> GetAuthor(string chatId, string authorId, bool inherit, CancellationToken cancellationToken)
        => _service.GetAuthor(chatId, authorId, inherit, cancellationToken);

    [HttpGet, Publish]
    public Task<Presence> GetAuthorPresence(string chatId, string authorId, CancellationToken cancellationToken)
        => _service.GetAuthorPresence(chatId, authorId, cancellationToken);

    // Commands

    [HttpPost]
    public Task AddToContacts(IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken)
        => _service.AddToContacts(command, cancellationToken);

    [HttpPost]
    public Task CreateChatAuthors(IChatAuthors.CreateChatAuthorsCommand command, CancellationToken cancellationToken)
        => _service.CreateChatAuthors(command, cancellationToken);
}
