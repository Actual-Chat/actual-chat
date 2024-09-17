using Microsoft.AspNetCore.Mvc;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Bot.Tools.Context;
using ActualChat.Chat;

namespace ActualChat.MLSearch.Bot.Tools;

[BotTools]
[ApiController]
[Route("api/bot/chat")]
[Produces("application/json")]
public sealed class ChatToolsController(IChatsBackend chats, IBotToolsContextHandler botToolsContext) : ControllerBase
{
    public class ReadLastMessagesRequest {
        public string ChatId { get; init; }
        public uint Limit { get; init; }
    }
    // Note: 
    // This method returns ChatId
    [HttpPost("list")]
    public async Task<ActionResult<List<string>>> List(CancellationToken cancellationToken)
    {
        
        var context = botToolsContext.GetContext(Request);
        if (!context.IsValid) {
            throw new UnauthorizedAccessException();
        }
        if (!ChatId.TryParse(context.ConversationId, out var chatId)) {
            throw new InvalidOperationException("Malformed conversation id detected.");
        }
        if (!chatId.IsPeerChat(out var peerChatId)){
            throw new InvalidOperationException("Unsupported");
        }
        var userId = peerChatId.UserId1;
        if (userId == Constants.User.MLSearchBot.UserId) {
            userId = peerChatId.UserId2;
        }
        var chatsList = await chats.ListChatIdsForUser(
            userId,
            placeId: null,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
        return Ok(chatsList.ToList());
    }

// Note: 
    // This method returns ChatId
    [HttpPost("read-last-messages")]
    public async Task<ActionResult<List<ChatEntry>>> List([FromBody]ReadLastMessagesRequest query, CancellationToken cancellationToken)
    {
        var context = botToolsContext.GetContext(Request);
        if (!context.IsValid) {
            throw new UnauthorizedAccessException();
        }
        if (!ChatId.TryParse(context.ConversationId, out var botChatId)) {
            throw new InvalidOperationException("Malformed conversation id detected.");
        }
        if (!botChatId.IsPeerChat(out var peerChatId)){
            throw new InvalidOperationException("Unsupported");
        }
        if (!ChatId.TryParse(query.ChatId, out var parsedChatId)) {
            throw new InvalidOperationException("Malformed chat id detected.");
        }
        var userId = peerChatId.UserId1;
        if (userId == Constants.User.MLSearchBot.UserId) {
            userId = peerChatId.UserId2;
        }
        var chatsList = await chats.ListChatIdsForUser(
            userId,
            placeId: null,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
        var chatEtries = await chats.ListNewEntries(
            chatId: parsedChatId,
            minLocalIdExclusive: -1,
            limit: (int)query.Limit,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);

        return Ok(chatEtries.ToList());
    }

    
}
