using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class AuthorsController : ControllerBase, IChatAuthors
{
    private readonly IChatAuthors _service;
    private readonly ICommander _commander;

    public AuthorsController(IChatAuthors service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<ChatAuthor?> Get(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatAuthorFull?> GetOwn(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetOwn(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatAuthorFull?> GetFull(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => _service.GetFull(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListChatIds(Session session, CancellationToken cancellationToken)
        => _service.ListChatIds(session, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken)
        => _service.ListAuthorIds(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken)
        => _service.ListUserIds(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<Presence> GetAuthorPresence(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => _service.GetAuthorPresence(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<bool> CanAddToContacts(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => _service.CanAddToContacts(session, chatId, authorId, cancellationToken);

    // Commands

    [HttpPost]
    public Task AddToContacts([FromBody] IChatAuthors.AddToContactsCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task CreateChatAuthors([FromBody] IChatAuthors.CreateChatAuthorsCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetAvatar([FromBody] IChatAuthors.SetAvatarCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
