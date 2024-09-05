using ActualChat.Chat;
using ActualChat.Search;

namespace ActualChat.Testing.Host;

public static class EntrySearchResultUtil
{
    public static EntrySearchResult BuildSearchResult(this ChatEntry entry)
        => new (entry.Id, SearchMatch.New(entry.Content));
    public static IEnumerable<EntrySearchResult> BuildSearchResults(this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.BuildSearchResult());
}
