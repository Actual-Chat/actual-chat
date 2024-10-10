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
    [Flags]
    public enum SearchType
    {
        None = 0,
        Public = 1,
        Private = 2,
        General = Public | Private,
    }

    public sealed class SearchQueryRequest {
        public const int MaxLimit = 3;
        public required string Text { get; init; }
        public required SearchType SearchType { get; init; }
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

    [HttpPost("chats")]
    public async Task<ActionResult<List<SearchQueryDocumentResult>>> FindChats(
        [FromBody] SearchQueryRequest search,
        CancellationToken cancellationToken)
    {
        var context = botToolsContext.GetContext(Request);
        if (!context.IsValid
            || (context.ConversationId is var conversationId && conversationId.IsNullOrEmpty())
            || (context.UserId is var contextUserId && contextUserId.IsNullOrEmpty())) {
            throw new UnauthorizedAccessException();
        }

        var chatId = ChatId.TryParse(conversationId, out var parsedChatId)
            ? parsedChatId
            : throw new InvalidOperationException("Malformed conversation id detected.");
        var userId = UserId.TryParse(contextUserId, out var parsedUserId)
            ? parsedUserId
            : throw new InvalidOperationException("Malformed user id detected.");

        var limit = Math.Clamp(search.Limit ?? 1, 1, SearchQueryRequest.MaxLimit);

        var query = new SearchQuery {
            Filters = [
                await filters.Semantic(search.Text, cancellationToken).ConfigureAwait(false),
                await filters.Keyword(search.Text, cancellationToken).ConfigureAwait(false),
                await filters.Chat(ConfigureFilter, cancellationToken).ConfigureAwait(false),
            ],
            Limit = limit,
        };

        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);

        return searchResult.Documents
            .Where(e => !e.IsNone && !e.Document.Metadata.ChatEntries.IsDefaultOrEmpty)
            .Select(e => {
                var chatEntryId = e.Document.Metadata.ChatEntries[0].Id;
                var link = Links.Chat(chatEntryId);
                return new SearchQueryDocumentResult {
                    LocalUrl = link,
                    Document = e,
                };
            })
            .ToList();

        ChatSet ConfigureFilter(ChatSet chats)
        {
            chats = (search.SearchType & SearchType.Public) > 0 ? chats.Public() : chats;
            chats = (search.SearchType & SearchType.Private) > 0 ? chats.Private(userId) : chats;
            return chats.Exclude([chatId]);
        }
    }
}
