using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public sealed class AuthorsController : ControllerBase, IAuthors
{
    private IAuthors Service { get; }
    private ICommander Commander { get; }

    public AuthorsController(IAuthors service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<Author?> Get(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
        => Service.Get(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorFull?> GetOwn(Session session, ChatId chatId, CancellationToken cancellationToken)
        => Service.GetOwn(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorFull?> GetFull(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
        => Service.GetFull(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<Account?> GetAccount(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
        => Service.GetAccount(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<Presence> GetPresence(Session session, ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
        => Service.GetPresence(session, chatId, authorId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<AuthorId>> ListAuthorIds(Session session, ChatId chatId, CancellationToken cancellationToken)
        => Service.ListAuthorIds(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<UserId>> ListUserIds(Session session, ChatId chatId, CancellationToken cancellationToken)
        => Service.ListUserIds(session, chatId, cancellationToken);

    // Commands

    [HttpPost]
    public Task<AuthorFull> Join(IAuthors.JoinCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task Leave(IAuthors.LeaveCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task Invite([FromBody] IAuthors.InviteCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task SetAvatar([FromBody] IAuthors.SetAvatarCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
