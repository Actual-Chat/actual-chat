using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;

namespace ActualChat.MLSearch.Bot.Services;

internal sealed class SearchResult
{
    public required string Text { get; init; }
    public required string Link { get; init; }
}

internal interface ISearchPlugin
{
    Task<SearchResult[]> Find(
        string queryText,
        SearchType searchType,
        string conversationId,
        string userId,
        int limit = 1,
        CancellationToken cancellationToken = default
    );
}

internal sealed class SearchPlugin(
    IFilters filters,
    ISearchEngine<ChatSlice> searchEngine
) : ISearchPlugin
{
    public const int MaxLimit = 5;

    public async Task<SearchResult[]> Find(
        string queryText,
        SearchType searchType,
        string conversationId,
        string userId,
        int limit = 1,
        CancellationToken cancellationToken = default
    )
    {
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
            Limit = Math.Clamp(limit, 1, MaxLimit),
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
