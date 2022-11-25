using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ChatsController : ControllerBase, IChats
{
    private IChats Service { get; }
    private ICommander Commander { get; }

    public ChatsController(IChats service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    // NOTE(AY): We use string? chatId here to make sure this method can be invoked with "" -
    // and even though this is not valid parameter value, we want it to pass ASP.NET Core
    // parameter validation & trigger the error later.
    public Task<Chat?> Get(Session session, string? chatId, CancellationToken cancellationToken)
        => Service.Get(session, chatId ?? "", cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorRules> GetRules(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
        => Service.GetRules(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatSummary?> GetSummary(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
        => Service.GetSummary(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
        => Service.GetEntryCount(session, chatId, entryType, idTileRange, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
        => Service.GetTile(session, chatId, entryType, idTileRange, cancellationToken);

    [HttpGet, Publish]
    public Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
        => Service.GetIdRange(session, chatId, entryType, cancellationToken);

    [HttpGet, Publish]
    public Task<bool> HasInvite(Session session, string chatId, CancellationToken cancellationToken)
        => Service.HasInvite(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<bool> CanJoin(Session session, string chatId, CancellationToken cancellationToken)
        => Service.CanJoin(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Author>> ListMentionableAuthors(Session session, string chatId, CancellationToken cancellationToken)
        => Service.ListMentionableAuthors(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatEntry?> FindNext(
        Session session,
        string chatId,
        long? startEntryId,
        string text,
        CancellationToken cancellationToken)
        => Service.FindNext(session, chatId,
            startEntryId,
            text,
            cancellationToken);

    // Commands

    [HttpPost]
    public Task AddMembers([FromBody] IChats.AddMembersCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task<Chat> Change([FromBody] IChats.ChangeCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task<Unit> Join([FromBody] IChats.JoinCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task Leave([FromBody] IChats.LeaveCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task<ChatEntry> UpsertTextEntry([FromBody] IChats.UpsertTextEntryCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task RemoveTextEntry([FromBody] IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
