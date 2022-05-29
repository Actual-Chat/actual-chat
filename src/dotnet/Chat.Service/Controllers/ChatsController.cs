using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatsController : ControllerBase, IChats
{
    private readonly IChats _service;

    public ChatsController(IChats service)
        => _service = service;

    [HttpGet, Publish]
    public Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken)
        => _service.GetChats(session, cancellationToken);

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
    public Task<ChatAuthorPermissions> GetPermissions(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
        => _service.GetPermissions(session, chatId, cancellationToken);

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

    [HttpGet]
    public Task<MentionCandidate[]> GetMentionCandidates(Session session, string chatId, CancellationToken cancellationToken)
        => _service.GetMentionCandidates(session, chatId, cancellationToken);

    // Commands

    [HttpPost]
    public Task<Chat> CreateChat([FromBody] IChats.CreateChatCommand command, CancellationToken cancellationToken)
        => _service.CreateChat(command, cancellationToken);

    [HttpPost]
    public Task<Unit> UpdateChat(IChats.UpdateChatCommand command, CancellationToken cancellationToken)
        => _service.UpdateChat(command, cancellationToken);

    [HttpPost]
    public Task<Unit> JoinChat([FromBody] IChats.JoinChatCommand command, CancellationToken cancellationToken)
        => _service.JoinChat(command, cancellationToken);

    [HttpPost]
    public Task<ChatEntry> CreateTextEntry([FromBody] IChats.CreateTextEntryCommand command, CancellationToken cancellationToken)
        => _service.CreateTextEntry(command, cancellationToken);

    [HttpPost]
    public Task RemoveTextEntry(IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken)
        => _service.RemoveTextEntry(command, cancellationToken);
}
