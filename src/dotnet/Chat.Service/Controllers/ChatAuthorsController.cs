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
    public Task<ChatAuthor?> GetOwnAuthor(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetOwnAuthor(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<Symbol> GetOwnPrincipalId(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetOwnPrincipalId(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListOwnChatIds(Session session, CancellationToken cancellationToken)
        => _service.ListOwnChatIds(session, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken)
        => _service.ListAuthorIds(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken)
        => _service.ListUserIds(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<Author?> GetAuthor(string chatId, string authorId, bool inherit, CancellationToken cancellationToken)
        => _service.GetAuthor(chatId, authorId, inherit, cancellationToken);

    [HttpGet, Publish]
    public Task<Presence> GetAuthorPresence(string chatId, string authorId, CancellationToken cancellationToken)
        => _service.GetAuthorPresence(chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<bool> CanAddToContacts(Session session, string chatPrincipalId, CancellationToken cancellationToken)
        => _service.CanAddToContacts(session, chatPrincipalId, cancellationToken);

    // Commands

    [HttpPost]
    public Task AddToContacts(IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken)
        => _service.AddToContacts(command, cancellationToken);

    [HttpPost]
    public Task CreateChatAuthors(IChatAuthors.CreateChatAuthorsCommand command, CancellationToken cancellationToken)
        => _service.CreateChatAuthors(command, cancellationToken);
}
