using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;

namespace ActualChat.Testing.Host;

public static class EntrySearchResultUtil
{
    public static IEnumerable<FoundItem> BuildFoundEntries(
        this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.BuildFoundEntry());

    public static FoundItem BuildFoundEntry(this ChatEntry entry)
        => new (entry.BuildSearchResult(), SearchScope.Messages);

    public static EntrySearchResult BuildSearchResult(this ChatEntry entry)
        => new (entry.Id, SearchMatch.New(entry.Content));
    public static IEnumerable<EntrySearchResult> BuildSearchResults(this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.BuildSearchResult());
}
