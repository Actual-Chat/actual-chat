using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public sealed class ChatsController : ControllerBase, IChats
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
    public Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken)
        => Service.Get(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<AuthorRules> GetRules(Session session, ChatId chatId, CancellationToken cancellationToken)
        => Service.GetRules(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatNews> GetNews(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
        => Service.GetNews(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
        => Service.GetEntryCount(session, chatId, entryKind, idTileRange, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
        => Service.GetTile(session, chatId, entryKind, idTileRange, cancellationToken);

    [HttpGet, Publish]
    public Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken)
        => Service.GetIdRange(session, chatId, entryKind, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Author>> ListMentionableAuthors(Session session, ChatId chatId, CancellationToken cancellationToken)
        => Service.ListMentionableAuthors(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatEntry?> FindNext(
        Session session,
        ChatId chatId,
        long? startEntryId,
        string text,
        CancellationToken cancellationToken)
        => Service.FindNext(session, chatId,
            startEntryId,
            text,
            cancellationToken);

    // Commands

    [HttpPost]
    public Task<Chat> Change([FromBody] IChats.ChangeCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task<ChatEntry> UpsertTextEntry([FromBody] IChats.UpsertTextEntryCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task RemoveTextEntry([FromBody] IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

    [HttpPost]
    public Task<Chat> CreateFromTemplate([FromBody] IChats.CreateFromTemplateCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);

}
