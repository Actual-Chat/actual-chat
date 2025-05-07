using ActualChat.Chat;
using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Mathematics;

namespace ActualChat.Testing.Host;

public static class EntrySearchResultExt
{
    public static List<FoundItem> BuildFoundEntries(this IEnumerable<ChatEntry> entries, IReadOnlyList<string> highlightedWords, string uniquePart = "")
        => entries.Select(x => x.BuildFoundEntry(highlightedWords, uniquePart)).ToList();

    public static FoundItem BuildFoundEntry(this ChatEntry entry, IReadOnlyList<string> highlightedWords, string uniquePart = "")
        => new (entry.BuildSearchResult(highlightedWords, uniquePart), SearchScope.Messages, false);

    public static List<EntrySearchResult> BuildSearchResults(this IEnumerable<ChatEntry> entries, IReadOnlyList<string> highlightedWords, string uniquePart = "")
        => entries.Select(x => x.BuildSearchResult(highlightedWords, uniquePart)).ToList();
    public static EntrySearchResult BuildSearchResult(this ChatEntry entry, IReadOnlyList<string> highlightedWords, string uniquePart = "", params Range<int>[] searchMatchRanges)
        => entry.Id.BuildSearchResult(entry.Content, highlightedWords, uniquePart, searchMatchRanges);

    public static EntrySearchResult BuildSearchResult(this ChatEntryId entryId, string highlight, IReadOnlyList<string> highlightedWords, string uniquePart, params Range<int>[] searchMatchRanges)
        => new (entryId.ToTextEntryId(), searchMatchRanges.BuildSearchMatch(highlight, uniquePart)) {
            HighlightedWords = highlightedWords.Append(uniquePart).Where(x => !x.IsNullOrEmpty()).ToApiSet(),
        };
}
