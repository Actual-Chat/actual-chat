using Microsoft.AspNetCore.Mvc;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Bot.Tools.Context;

namespace ActualChat.MLSearch.Bot.Tools;

[BotTools]
[ApiController]
[Route("api/bot/search")]
[Produces("application/json")]
public sealed class SearchToolsController(
    IFilters filters,
    ISearchEngine<ChatSlice> searchEngine,
    IBotToolsContextHandler botToolsContext) : ControllerBase
{
    public sealed class SearchQueryRequest {
        public const int MaxLimit = 3;
        public required string Text { get; init; }
        public int? Limit { get; set; } = 1;
    }

    // Note:
    // Check if we want to use M6T.Core.TupleModelBinder.
    // The issue of using it right now: no swagger support.
    public sealed class SearchQueryDocumentResult {
        [JsonIgnore]
        public LocalUrl LocalUrl { get; init; }

        [JsonInclude]
        public string Link => LocalUrl.Value;
        public RankedDocument<ChatSlice> Document { get; init; }
    }

    [HttpPost("public-chats")]
    public async Task<ActionResult<List<SearchQueryDocumentResult>>> PublicChatsText([FromBody]SearchQueryRequest search, CancellationToken cancellationToken)
    {
        var context = botToolsContext.GetContext(Request);
        if (!context.IsValid || (context.ConversationId is var conversationId && string.IsNullOrEmpty(conversationId))) {
            throw new UnauthorizedAccessException();
        }
        if (!ChatId.TryParse(conversationId, out var chatId)) {
            throw new InvalidOperationException("Malformed conversation id detected.");
        }

        var limit = search.Limit ?? 1;
        // Add limit constraints.
        if (limit < 1) {
            limit = 1;
        }
        if (limit > SearchQueryRequest.MaxLimit) {
            limit = SearchQueryRequest.MaxLimit;
        }
        var query = new SearchQuery() {
            Filters = [
                await filters.Semantic(search.Text, cancellationToken).ConfigureAwait(false),
                await filters.Keyword(search.Text, cancellationToken).ConfigureAwait(false),
                await filters.Chat(chats => chats.Public().Exclude([chatId]), cancellationToken).ConfigureAwait(false),
            ],
            Limit = limit,
        };
        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        // TODO: (?) Error handling
        return searchResult.Documents
            .Where(e => !e.IsNone && !e.Document.Metadata.ChatEntries.IsDefaultOrEmpty)
            .Select(e => {
                // This must throw if result
                var chatEntryId = e.Document.Metadata.ChatEntries[0].Id;
                var link = Links.Chat(chatEntryId);
                return new SearchQueryDocumentResult {
                    LocalUrl = link,
                    Document = e,
                };
            })
            .ToList();
    }

    [HttpPost("my-chats")]
    public async Task<ActionResult<List<RankedDocument<ChatSlice>>>> PrivateChatsText([FromBody]SearchQueryRequest search, CancellationToken cancellationToken)
    {
        var context = botToolsContext.GetContext(Request);
        if (!context.IsValid
            || (context.ConversationId is var conversationId && string.IsNullOrEmpty(conversationId))
            || (context.UserId is var contextUserId && string.IsNullOrEmpty(contextUserId))) {
            throw new UnauthorizedAccessException();
        }
        if (!ChatId.TryParse(conversationId, out var chatId) || !UserId.TryParse(contextUserId, out var userId)) {
            throw new InvalidOperationException("Malformed conversation id detected.");
        }

        var limit = search.Limit;
        // Add limit constraints.
        if (limit < 1) {
            limit = 1;
        }
        if (limit > SearchQueryRequest.MaxLimit) {
            limit = SearchQueryRequest.MaxLimit;
        }
        var query = new SearchQuery() {
            Filters = [
                await filters.Semantic(search.Text, cancellationToken).ConfigureAwait(false),
                await filters.Keyword(search.Text, cancellationToken).ConfigureAwait(false),
                await filters.Chat(
                    chats => chats.Private(userId).Exclude([chatId]),
                    cancellationToken
                ).ConfigureAwait(false),
            ],
            Limit = limit,
        };
        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        var documents = searchResult.Documents;
        // TODO: Error handling

        return documents.Select(e=>e).ToList();
    }
}
