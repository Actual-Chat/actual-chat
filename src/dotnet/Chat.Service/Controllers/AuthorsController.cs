using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class AuthorsController : ControllerBase, IAuthors
{
    private readonly IAuthors _service;
    private readonly ICommander _commander;

    public AuthorsController(IAuthors service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<Author?> Get(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorFull?> GetOwn(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetOwn(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorFull?> GetFull(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => _service.GetFull(session, chatId, authorId, cancellationToken);

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
    public Task<Presence> GetAuthorPresence(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => _service.GetAuthorPresence(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<bool> CanAddToContacts(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => _service.CanAddToContacts(session, chatId, authorId, cancellationToken);

    // Commands

    [HttpPost]
    public Task AddToContacts([FromBody] IAuthors.AddToContactsCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task CreateAuthors([FromBody] IAuthors.CreateAuthorsCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetAvatar([FromBody] IAuthors.SetAvatarCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
