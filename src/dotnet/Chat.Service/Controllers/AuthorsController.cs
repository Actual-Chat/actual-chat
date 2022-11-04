using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class AuthorsController : ControllerBase, IAuthors
{
    private IAuthors Service { get; }
    private ICommander Commander { get; }

    public AuthorsController(IAuthors service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<Author?> Get(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => Service.Get(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorFull?> GetOwn(Session session, string chatId, CancellationToken cancellationToken)
        => Service.GetOwn(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorFull?> GetFull(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => Service.GetFull(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<Account?> GetAccount(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => Service.GetAccount(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListOwnChatIds(Session session, CancellationToken cancellationToken)
        => Service.ListOwnChatIds(session, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, CancellationToken cancellationToken)
        => Service.ListAuthorIds(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Symbol>> ListUserIds(Session session, string chatId, CancellationToken cancellationToken)
        => Service.ListUserIds(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<Presence> GetAuthorPresence(Session session, string chatId, string authorId, CancellationToken cancellationToken)
        => Service.GetAuthorPresence(session, chatId, authorId, cancellationToken);

    // Commands

    [HttpPost]
    public Task CreateAuthors([FromBody] IAuthors.CreateAuthorsCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetAvatar([FromBody] IAuthors.SetAvatarCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
