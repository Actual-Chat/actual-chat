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
    IAuthorsBackend authors,
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
                await filters.Chat(chats => chats.Public(), cancellationToken).ConfigureAwait(false),
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
        if (!context.IsValid || (context.ConversationId is var conversationId && string.IsNullOrEmpty(conversationId))) {
            throw new UnauthorizedAccessException();
        }
        if (!ChatId.TryParse(conversationId, out var chatId)) {
            throw new InvalidOperationException("Malformed conversation id detected.");
        }

        var ownerAuthorIds = await authors.ListOwnerAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var ownerAuthorId = ownerAuthorIds.OrderBy(authorId => authorId.LocalId).FirstOrDefault(AuthorId.None);
        if (ownerAuthorId.IsNone) {
            throw new InvalidOperationException("No owner author found for the search chat.");
        }
        var ownerAuthor = await authors
            .Get(chatId, ownerAuthorId, AuthorsBackend_GetAuthorOption.Full, cancellationToken)
            .ConfigureAwait(false);
        var ownerAuthorUserId = ownerAuthor!.UserId;

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
                await filters.Chat(chats => chats.Private(ownerAuthorUserId), cancellationToken).ConfigureAwait(false),
            ],
            Limit = limit,
        };
        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        var documents = searchResult.Documents;
        // TODO: Error handling

        return documents.Select(e=>e).ToList();
    }
}
