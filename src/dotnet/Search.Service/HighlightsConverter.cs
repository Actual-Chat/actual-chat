using OpenSearch.Client;

namespace ActualChat.Search;

public static class HighlightsConverter
{
    public const string PreTag = "<em>";
    public const string PostTag = "</em>";
    private static readonly string FullNameField = "fullName";
    private static readonly string TitleField = "title";

    public static SearchMatch GetSearchMatch(this IHit<IndexedUserContact> hit)
    {
        var highlight = hit.Highlight[FullNameField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.New(hit.Source.FullName);

        return ToSearchMatch(hit.Source.FullName, highlight, hit.Score ?? 0.1);
    }

    public static SearchMatch GetSearchMatch(this IHit<IndexedChatContact> hit)
    {
        var highlight = hit.Highlight[TitleField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.New(hit.Source.Title);

        return ToSearchMatch(hit.Source.Title, highlight, hit.Score ?? 0.1);
    }

    public static SearchMatch ToSearchMatch(string plain, string highlight, double score)
    {
        var searchMatchParts = FindRanges(highlight).Select(x => new SearchMatchPart(x, 1)).ToArray();
        return new (plain, score, searchMatchParts);
    }

    private static IEnumerable<Range<int>> FindRanges(string highlightedString)
    {
        var position = 0;
        var plainOffset = 0;
        while (position < highlightedString.Length) {
            var iStart = highlightedString.OrdinalIndexOf(PreTag, position);
            if (iStart < 0)
                yield break;

            iStart += PreTag.Length;
            var iEnd = highlightedString.OrdinalIndexOf(PostTag, iStart);
            if (iEnd < 0)
                yield break;

            position = iEnd + PostTag.Length;
            plainOffset += PreTag.Length;
            yield return new (iStart - plainOffset, iEnd - plainOffset);

            plainOffset += PostTag.Length;
        }
    }
}
