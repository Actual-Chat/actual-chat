using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Mathematics;

namespace ActualChat.Testing.Host;

public static class EntrySearchResultUtil
{
    public static IEnumerable<FoundItem> BuildFoundEntries(
        this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.BuildFoundEntry());

    public static FoundItem BuildFoundEntry(this ChatEntry entry)
        => new (entry.BuildSearchResult(), SearchScope.Messages);

    public static IEnumerable<EntrySearchResult> BuildSearchResults(this IEnumerable<ChatEntry> entries)
        => entries.Select(x => x.BuildSearchResult());
    public static EntrySearchResult BuildSearchResult(this ChatEntry entry, string uniquePart = "", params Range<int>[] searchMatchRanges)
        => entry.Id.BuildSearchResult(entry.Content, uniquePart, searchMatchRanges);

    public static EntrySearchResult BuildSearchResult(this ChatEntryId entryId, string highlight, string uniquePart, params Range<int>[] searchMatchRanges)
        => new (entryId, searchMatchRanges.BuildSearchMatch(highlight, uniquePart));
}
