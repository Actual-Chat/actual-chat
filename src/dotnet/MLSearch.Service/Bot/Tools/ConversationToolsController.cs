using System.Text;
using ActualChat.Hashing;
using ActualChat.Security;
using ActualChat.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Documents;
using ActualChat.Chat;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Cors;
using ActualChat.MLSearch.Bot.Tools.Context;

namespace ActualChat.MLSearch.Bot.Tools;

[BotTools]
[ApiController]
[Route("api/bot/conversation")]
[Produces("application/json")]
public sealed class ConversationToolsController(ICommander commander, IBotToolsContextHandler botToolsContext): ControllerBase
{
    public sealed class Reply {
        public required string Text {get; init;}
    }

    public sealed class ForwardChatLinks {
        public string? Comment {get; set;}
        public List<string>? Links { get; init; }
    }


    [HttpPost("reply")]
    public async Task ReplyAction([FromBody]Reply reply, CancellationToken cancellationToken) {
        var context = botToolsContext.GetContext(Request);
        if (!context.IsValid) {
            throw new UnauthorizedAccessException();
        }
        string? conversationId = context.ConversationId;
        if (conversationId.IsNullOrEmpty()){
            throw new UnauthorizedAccessException();
        }
        var chatId = ChatId.Parse(conversationId);
        AuthorId botId = new(chatId, Constants.User.MLSearchBot.AuthorLocalId, AssumeValid.Option);
        var textEntryId = new TextEntryId(chatId, 0, AssumeValid.Option);
        var upsertCommand = new ChatsBackend_ChangeEntry(
            textEntryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = botId,
                Content = reply.Text,
            }));
        await commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        return;
    }

    [HttpPost("forward-chat-links")]
    public async Task ForwardChatLinksAction([FromBody]ForwardChatLinks reply, CancellationToken cancellationToken) {
        var context = botToolsContext.GetContext(Request);
        if (!context.IsValid) {
            throw new UnauthorizedAccessException();
        }
        string? conversationId = context.ConversationId;
        if (conversationId.IsNullOrEmpty()){
            throw new UnauthorizedAccessException();
        }
        var chatId = ChatId.Parse(conversationId);
        AuthorId botId = new(chatId, Constants.User.MLSearchBot.AuthorLocalId, AssumeValid.Option);
        var textEntryId = new TextEntryId(chatId, 0, AssumeValid.Option);
        var upsertCommand = new ChatsBackend_ChangeEntry(
            textEntryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = botId,
                Content = reply.Comment + " >> " + String.Join( 
                    " + ", 
                    reply.Links ?? new List<string>()
                ),
            }));
        await commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        return;
    }


}