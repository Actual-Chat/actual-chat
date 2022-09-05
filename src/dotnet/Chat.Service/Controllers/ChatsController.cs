using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ChatsController : ControllerBase, IChats
{
    private readonly IChats _service;
    private readonly ICommander _commander;

    public ChatsController(IChats service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    // NOTE(AY): We use string? chatId here to make sure this method can be invoked with "" -
    // and even though this is not valid parameter value, we want it to pass ASP.NET Core
    // parameter validation & trigger the error later.
    public Task<Chat?> Get(Session session, string? chatId, CancellationToken cancellationToken)
        => _service.Get(session, chatId ?? "", cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Chat>> List(Session session, CancellationToken cancellationToken)
        => _service.List(session, cancellationToken);

    [HttpGet, Publish]
    public Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
        => _service.GetEntryCount(session, chatId, entryType, idTileRange, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
        => _service.GetTile(session, chatId, entryType, idTileRange, cancellationToken);

    [HttpGet, Publish]
    public Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
        => _service.GetIdRange(session, chatId, entryType, cancellationToken);

    [HttpGet, Publish]
    public Task<Range<long>> GetLastIdTile0(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
        => _service.GetIdRange(session, chatId, entryType, cancellationToken);

    [HttpGet, Publish]
    public Task<Range<long>> GetLastIdTile1(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
        => _service.GetIdRange(session, chatId, entryType, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatAuthorRules> GetRules(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
        => _service.GetRules(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<bool> CanJoin(Session session, string chatId, CancellationToken cancellationToken)
        => _service.CanJoin(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        Session session, string chatId, long entryId,
        CancellationToken cancellationToken)
        => _service.GetTextEntryAttachments(session, chatId, entryId, cancellationToken);

    [HttpGet, Publish]
    public Task<bool> CanSendPeerChatMessage(Session session, string chatPrincipalId, CancellationToken cancellationToken)
        => _service.CanSendPeerChatMessage(session, chatPrincipalId, cancellationToken);

    [HttpGet, Publish]
    public Task<string?> GetPeerChatId(Session session, string chatPrincipalId, CancellationToken cancellationToken)
        => _service.GetPeerChatId(session, chatPrincipalId, cancellationToken);

    [HttpGet, Publish]
    public Task<UserContact?> GetPeerChatContact(Session session, Symbol chatId, CancellationToken cancellationToken)
        => _service.GetPeerChatContact(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<Author>> ListMentionableAuthors(Session session, string chatId, CancellationToken cancellationToken)
        => _service.ListMentionableAuthors(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatEntry?> FindNext(
        Session session,
        string chatId,
        long? startEntryId,
        string text,
        CancellationToken cancellationToken)
        => _service.FindNext(session, chatId,
            startEntryId,
            text,
            cancellationToken);

    // Commands

    [HttpPost]
    public Task<Chat?> ChangeChat([FromBody] IChats.ChangeChatCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task<Unit> JoinChat([FromBody] IChats.JoinChatCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task LeaveChat([FromBody] IChats.LeaveChatCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task<ChatEntry> UpsertTextEntry([FromBody] IChats.UpsertTextEntryCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);

    [HttpPost]
    public Task RemoveTextEntry([FromBody] IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
