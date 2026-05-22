using ActualChat.Search;

namespace ActualChat.Testing.Host;

public static class SearchMatchExt
{
    public static SearchMatch BuildSearchMatch(
        this Range<int>[]? searchMatchPartRanges,
        string text,
        string highlightedSuffix = "")
    {
        if (searchMatchPartRanges.IsNullOrEmpty())
            return SearchMatch.Matchless(text);

        var searchMatchParts = searchMatchPartRanges.Select(x => new SearchMatchPart(x, 1));
        if (!highlightedSuffix.IsNullOrEmpty() && text.EndsWith(highlightedSuffix)) {
            var suffixRange = new Range<int>(text.Length - highlightedSuffix.Length, text.Length);
            searchMatchParts = searchMatchParts.Append(new SearchMatchPart(suffixRange, 1));
        }
        return new SearchMatch(text, 1, searchMatchParts.ToArray());
    }
}
