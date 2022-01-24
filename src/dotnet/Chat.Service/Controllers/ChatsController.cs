using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class ChatsController : ControllerBase, IChats
{
    private readonly IChats _chats;
    private readonly ISessionResolver _sessionResolver;

    public ChatsController(IChats chats, ISessionResolver sessionResolver)
    {
        _chats = chats;
        _sessionResolver = sessionResolver;
    }

    [HttpGet, Publish]
    public Task<Chat?> Get(Session session, string chatId, CancellationToken cancellationToken)
        => _chats.Get(session, chatId, cancellationToken);

    [HttpGet, Publish]
    public Task<Chat[]> GetChats(Session session, CancellationToken cancellationToken)
        => _chats.GetChats(session, cancellationToken);

    [HttpGet, Publish]
    public Task<InviteCodeCheckResult> CheckInviteCode(Session session, string inviteCode, CancellationToken cancellationToken)
        => _chats.CheckInviteCode(session, inviteCode, cancellationToken);

    [HttpGet, Publish]
    public Task<long> GetEntryCount(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        CancellationToken cancellationToken)
        => _chats.GetEntryCount(session, chatId, entryType, idTileRange, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatTile> GetTile(
        Session session,
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
        => _chats.GetTile(session, chatId, entryType, idTileRange, cancellationToken);

    [HttpGet, Publish]
    public Task<Range<long>> GetIdRange(
        Session session,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken)
        => _chats.GetIdRange(session, chatId, entryType, cancellationToken);

    [HttpGet, Publish]
    public Task<ChatPermissions> GetPermissions(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
        => _chats.GetPermissions(session, chatId, cancellationToken);

    // Commands

    [HttpPost]
    public Task<Chat> CreateChat([FromBody] IChats.CreateChatCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _chats.CreateChat(command, cancellationToken);
    }

    [HttpPost]
    public Task<Unit> UpdateChat(IChats.UpdateChatCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _chats.UpdateChat(command, cancellationToken);
    }

    [HttpPost]
    public Task<Unit> JoinPublicChat([FromBody] IChats.JoinPublicChatCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _chats.JoinPublicChat(command, cancellationToken);
    }

    [HttpPost]
    public Task<string> JoinWithInviteCode([FromBody] IChats.JoinWithInviteCodeCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _chats.JoinWithInviteCode(command, cancellationToken);
    }

    [HttpPost]
    public Task<string> GenerateInviteCode([FromBody] IChats.GenerateInviteCodeCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _chats.GenerateInviteCode(command, cancellationToken);
    }

    [HttpPost]
    public Task<ChatEntry> CreateTextEntry([FromBody] IChats.CreateTextEntryCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _chats.CreateTextEntry(command, cancellationToken);
    }

    [HttpPost]
    public Task RemoveTextEntry(IChats.RemoveTextEntryCommand command, CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _chats.RemoveTextEntry(command, cancellationToken);
    }
}
