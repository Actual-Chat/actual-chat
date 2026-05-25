using ActualChat.Search;

namespace ActualChat.Testing.Host;

public static class SearchMatchExt
{
    public static SearchMatch BuildSearchMatch(
        this Range<int>[]? searchMatchPartRanges,
        string fullName,
        string uniquePart = "")
    {
        if (searchMatchPartRanges is null || searchMatchPartRanges.Length == 0)
            return SearchMatch.New(fullName);

        Range<int>[] uniquePartRanges = !uniquePart.IsNullOrEmpty() && fullName.EndsWith(uniquePart)
            ? uniquePartRanges = [(fullName.Length - uniquePart.Length, fullName.Length)]
            : [];

        var searchMatchParts = searchMatchPartRanges.Concat(uniquePartRanges)
            .Select(x => new SearchMatchPart(x, 1))
            .ToArray();
        return new SearchMatch(fullName, 1, searchMatchParts);
    }
}
