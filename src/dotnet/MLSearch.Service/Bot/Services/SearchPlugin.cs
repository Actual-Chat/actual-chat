using System.ComponentModel;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using Microsoft.SemanticKernel;

namespace ActualChat.MLSearch.Bot.Services;

internal sealed class SearchResult
{
    public required string Text { get; init; }
    public required string Link { get; init; }
}

internal sealed class SearchPlugin(
    IFilters filters,
    ISearchEngine<ChatSlice> searchEngine
)
{
    public const int MaxLimit = 5;

    [KernelFunction]
    [Description("Perform a search for content related to the specified query")]
    public async Task<SearchResult[]> Find(
        [Description("What to search for.")] string queryText,
        [Description("Type of the search to run.")] SearchType searchType,
        [Description("ID of ongoing search conversation.")] string conversationId,
        [Description("ID of the user who runs the search.")] string userId,
        [Description("Limit to the number of returned results.")] int? limit = 1
    )
    {
        var cancellationToken = CancellationToken.None;
        var chatId = ChatId.TryParse(conversationId, out var parsedChatId)
            ? parsedChatId
            : throw new InvalidOperationException("Malformed conversation id detected.");
        var principalId = UserId.TryParse(userId, out var parsedUserId)
            ? parsedUserId
            : throw new InvalidOperationException("Malformed user id detected.");

        var query = new SearchQuery {
            Filters = [
                await filters.Semantic(queryText, cancellationToken).ConfigureAwait(false),
                await filters.Keyword(queryText, cancellationToken).ConfigureAwait(false),
                await filters.Chat(ConfigureFilter, cancellationToken).ConfigureAwait(false),
            ],
            Limit = Math.Clamp(limit ?? 1, 1, MaxLimit),
        };

        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);

        return searchResult.Documents
            .Where(e => !e.IsNone && !e.Document.Metadata.ChatEntries.IsDefaultOrEmpty)
            .Select(e => {
                var chatEntryId = e.Document.Metadata.ChatEntries[0].Id;
                var link = Links.Chat(chatEntryId);
                return new SearchResult {
                    Link = link,
                    Text = e.Document.Text,
                };
            })
            .ToArray();

        ChatSet ConfigureFilter(ChatSet chats)
        {
            chats = (searchType & SearchType.Public) > 0 ? chats.Public() : chats;
            chats = (searchType & SearchType.Private) > 0 ? chats.Private(principalId) : chats;
            return chats.Exclude([chatId]);
        }
    }
}
