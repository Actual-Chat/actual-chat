using Microsoft.AspNetCore.Mvc;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Bot.Tools.Context;

namespace ActualChat.MLSearch.Bot.Tools;

[BotTools]
[ApiController]
[Route("api/bot/search")]
[Produces("application/json")]
public sealed class SearchToolsController(ISearchEngine<ChatSlice> searchEngine, IBotToolsContextHandler botToolsContext) : ControllerBase
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
        var limit = search.Limit.GetValueOrDefault(1);
        // Add limit constraints.
        if (limit < 1) {
            limit = 1;
        }
        if (limit > SearchQueryRequest.MaxLimit) {
            limit = SearchQueryRequest.MaxLimit;
        }
        var query = new SearchQuery() {
            Filters = [
                new SemanticFilter<ChatSlice>(search.Text),
                new KeywordFilter<ChatSlice>(search.Text.Split()),
                new ChatFilter() {
                    PublicChatInclusion = InclusionMode.IncludeStrictly,
                    SearchBotChatInclusion = InclusionMode.Exclude,
                }
            ],
            Limit = limit
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
                    Document = e
                };
            })
            .ToList();
    }

    [HttpPost("my-chats")]
    public async Task<ActionResult<List<RankedDocument<ChatSlice>>>> PrivateChatsText([FromBody]SearchQueryRequest search, CancellationToken cancellationToken)
    {
        var context = botToolsContext.GetContext(Request);
        if (!context.IsValid) {
            throw new UnauthorizedAccessException();
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
                new SemanticFilter<ChatSlice>(search.Text),
                new KeywordFilter<ChatSlice>(search.Text.Split()),
                new ChatFilter() {
                    PublicChatInclusion = InclusionMode.Include,
                    SearchBotChatInclusion = InclusionMode.Exclude,
                },
            ],
            Limit = limit,
        };
        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        var documents = searchResult.Documents;
        // TODO: Error handling
        return documents.Select(e=>e).ToList();
    }
}
