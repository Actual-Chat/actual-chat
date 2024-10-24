using ActualChat.Chat;
using System.Net.Http.Json;
using ActualChat.MLSearch.Bot.Tools.Context;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace ActualChat.MLSearch.Bot.External;

public sealed class ExternalChatbotSettings {
    public bool IsEnabled { get; set; }

    [Required]
    public required Uri WebHookUri { get; set; }

    public bool AllowPeerBotChat { get; set; }
}

/**
    A handler for incoming messages from a user.
    This class responsibility is to take those requests and forward it to a bot.
    It should generate JWT tokens together with the forwarded requests to allow
    the bot use internal tools. It is also possible that this bot would reply
    in asynchronous mode. That means that it will use the JWT token provided to
    send messages to a user through the internal API.
**/
internal class ExternalChatBotConversationHandler(
    IOptions<ExternalChatbotSettings> settings,
    IAuthorsBackend authors,
    IBotToolsContextHandler botToolsContextHandler,
    IHttpClientFactory httpClientFactory)
    : IBotConversationHandler, IComputeService
{
    public async Task ExecuteAsync(
        IReadOnlyCollection<ChatEntry>? updatedDocuments,
        IReadOnlyCollection<ChatEntryId>? deletedDocuments,
        CancellationToken cancellationToken = default)
    {
        var currentSettings = settings.Value;
        if (!currentSettings.IsEnabled)
            return;

        if (updatedDocuments == null)
            return;

        var lastUpdatedDocument = updatedDocuments.LastOrDefault();
        if (lastUpdatedDocument == null)
            return;

        var chatId = lastUpdatedDocument.ChatId;
        var authorId = lastUpdatedDocument.AuthorId;
        var botId = new AuthorId(chatId, Constants.User.Sherlock.AuthorLocalId, AssumeValid.Option);
        if (authorId == botId)
            return;

        var author = await authors
            .Get(chatId, authorId, AuthorsBackend_GetAuthorOption.Full, cancellationToken)
            .ConfigureAwait(false);
        var userId = author!.UserId;

        if (lastUpdatedDocument.Kind != ChatEntryKind.Text)
            // Can't react on anything besides text yet.
            return;

        // Minimal WebHook implementation.
        var client = httpClientFactory.CreateClient(nameof(ExternalChatBotConversationHandler));

        var url = currentSettings.WebHookUri;

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, url) {
            // Note:
            // The outmost "input" is part of the /invoke method of the langserve.
            // There are also "kwargs" and "config" parts available.
            // The inner <input> structure must be syncronized with the prompt used on the bot side.
            Content = JsonContent.Create(new {
                input = lastUpdatedDocument.Content,
            }),
        };
        botToolsContextHandler.SetContext(requestMessage, conversationId: chatId, userId: userId);
        var response = await client.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
        var resultContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        // Note: not expecting anything from the bot here. Might be worth logging.
    }
}
