
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.Media;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Net.Http.Headers;
using ActualChat.MLSearch.Bot.Tools;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using ActualChat.MLSearch.Bot.Tools.Context;

namespace ActualChat.MLSearch.Bot.External;


/**
    A handler for incoming messages from a user.
    This class responsibility is to take those requests and forward it to a bot.
    It should generate JWT tokens together with the forwarded requests to allow
    the bot use internal tools. It is also possible that this bot would reply
    in asyncronous mode. That means that it will use the JWT token provided to 
    send messages to a user through the internal API. 
**/
internal class ExternalChatBotConversationHandler(IBotToolsContextHandler botToolsContextHandler, ICommander commander, UrlMapper urlMapper, IChatsBackend chats, ISearchEngine<ChatSlice> searchEngine)
    : IBotConversationHandler, IComputeService
{
    public async Task ExecuteAsync(
        IReadOnlyCollection<ChatEntry>? updatedDocuments,
        IReadOnlyCollection<ChatEntryId>? deletedDocuments,
        CancellationToken cancellationToken = default)
    {
        if (updatedDocuments == null) {
            return;
        }

        var lastUpdatedDocument = updatedDocuments.LastOrDefault();
        if (lastUpdatedDocument == null)
            return;

        var chatId = lastUpdatedDocument.ChatId;
        AuthorId botId = new(chatId, Constants.User.MLSearchBot.AuthorLocalId, AssumeValid.Option);
        if (lastUpdatedDocument.AuthorId == botId)
            return;
        ///
        
        HttpClient client = new HttpClient();
        
        var url = "https://local.actual.chat/api/bot/conversation-tools/reply";

        HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
        
        requestMessage.Content = JsonContent.Create(new {
            text = "fpp"            
        });
        botToolsContextHandler.SetContext(requestMessage, conversationId: chatId);
        HttpResponseMessage response = client.SendAsync(requestMessage, cancellationToken).GetAwaiter().GetResult();
        var resultContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
