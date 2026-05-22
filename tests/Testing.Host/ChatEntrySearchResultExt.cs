using ActualChat.Search;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Testing.Host;

public static class ChatEntrySearchResultExt
{
    public static List<FoundItem> BuildFoundEntries(this IEnumerable<ChatEntry> entries, IReadOnlyList<string> highlightedWords, string testIsolationKey = "")
        => entries.Select(x => x.BuildFoundEntry(highlightedWords, testIsolationKey)).ToList();

    public static FoundItem BuildFoundEntry(this ChatEntry entry, IReadOnlyList<string> highlightedWords, string testIsolationKey = "")
        => new (entry.BuildSearchResult(highlightedWords, testIsolationKey), SearchScope.Messages, false);

    public static List<FoundChatEntry> BuildSearchResults(this IEnumerable<ChatEntry> entries, IReadOnlyList<string> highlightedWords, string testIsolationKey = "")
        => entries.Select(x => x.BuildSearchResult(highlightedWords, testIsolationKey)).ToList();
    public static FoundChatEntry BuildSearchResult(this ChatEntry entry, IReadOnlyList<string> highlightedWords, string testIsolationKey = "", params Range<int>[] searchMatchRanges)
        => entry.Id.BuildSearchResult(entry.Content, highlightedWords, testIsolationKey, searchMatchRanges);

    public static FoundChatEntry BuildSearchResult(this ChatEntryId entryId, string highlight, IReadOnlyList<string> highlightedWords, string testIsolationKey, params Range<int>[] searchMatchRanges)
        => new (entryId, searchMatchRanges.BuildSearchMatch(highlight, testIsolationKey)) {
            HighlightedWords = highlightedWords.Append(testIsolationKey)
                .Where(x => !x.IsNullOrEmpty())
                .ToApiSet(StringComparer.OrdinalIgnoreCase),
        };
}
