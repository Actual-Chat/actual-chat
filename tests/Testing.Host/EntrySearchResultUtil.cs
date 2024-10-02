using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;
using ActualLab.Mathematics;

namespace ActualChat.Testing.Host;

public static class EntrySearchResultUtil
{
    public static IEnumerable<FoundItem> BuildFoundEntries(
        this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.BuildFoundEntry());

    public static FoundItem BuildFoundEntry(this ChatEntry entry)
        => new (entry.BuildSearchResult(), SearchScope.Messages);

    public static EntrySearchResult BuildSearchResult(this ChatEntry entry, params Range<int>[] searchMatchRanges)
        => new (entry.Id, searchMatchRanges.BuildSearchMatch(entry.Content));

    public static EntrySearchResult BuildSearchResult(this ChatEntry entry, string highlight, params Range<int>[] searchMatchRanges)
        => new (entry.Id, searchMatchRanges.BuildSearchMatch(highlight));
    public static IEnumerable<EntrySearchResult> BuildSearchResults(this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.BuildSearchResult());
}
