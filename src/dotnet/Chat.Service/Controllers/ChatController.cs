using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatController : ControllerBase, IChatServiceFrontend
{
    private readonly IChatServiceFrontend _chats;
    private readonly ISessionResolver _sessionResolver;

    public ChatController(IChatServiceFrontend chats, ISessionResolver sessionResolver)
    {
        _chats = chats;
        _sessionResolver = sessionResolver;
    }

    // Commands

    [HttpPost]
    public Task<Chat> CreateChat([FromBody] IChatServiceFrontend.CreateChatCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _chats.CreateChat(command, cancellationToken);
    }

    [HttpPost]
    public Task<ChatEntry> CreateEntry([FromBody] IChatServiceFrontend.CreateEntryCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _chats.CreateEntry(command, cancellationToken);
    }

    // Queries

    [HttpGet, Publish]
    public Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken)
        => _chats.Get(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        Range<long>? idRange,
        CancellationToken cancellationToken)
        => _chats.GetEntryCount(session, chatId, idRange, cancellationToken);

    [HttpGet, Publish]
    public Task<ImmutableArray<ChatEntry>> GetEntries(
        Session session,
        ChatId chatId,
        Range<long> idRange,
        CancellationToken cancellationToken)
        => _chats.GetEntries(session, chatId, idRange, cancellationToken);

    [HttpGet, Publish]
    public Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
        => _chats.GetIdRange(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatPermissions> GetPermissions(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
        => _chats.GetPermissions(session, chatId, cancellationToken);
}
