using System.Text;
using ActualChat.Hashing;
using ActualChat.Security;
using ActualChat.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Documents;
using ActualChat.Chat;
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
    [HttpPost("public-chats")]
    public async Task<ActionResult<List<RankedDocument<ChatSlice>>>> PublicChatsText([FromBody]SearchQueryRequest search, CancellationToken cancellationToken)
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
        var documents = searchResult.Documents;
        // TODO: Error handling
        
        return documents.Select(e=>e).ToList();
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
                }
            ],
            Limit = limit
        };
        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        var documents = searchResult.Documents;
        // TODO: Error handling
        
        return documents.Select(e=>e).ToList();
    }
}