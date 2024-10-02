using ActualChat.Search;
using Cysharp.Text;
using OpenSearch.Client;
using IndexedEntry = ActualChat.MLSearch.Documents.IndexedEntry;

namespace ActualChat.MLSearch.Engine.OpenSearch.Extensions;

public static class HighlightsConverter
{
    public const string PreTag = "<em>";
    public const string PostTag = "</em>";
    public const string SkippedPartReplacement = "…";
    private static readonly string FullNameField = "fullName";
    private static readonly string TitleField = "title";
    private static readonly string ContentField = "content";

    public static SearchMatch GetSearchMatch(this IHit<IndexedUserContact> hit)
    {
        var highlight = hit.Highlight[FullNameField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.New(hit.Source.FullName);

        return ToSearchMatch(hit.Source.FullName, highlight, hit.Score ?? 1.0);
    }

    public static SearchMatch GetSearchMatch(this IHit<IndexedGroupChatContact> hit)
    {
        var highlight = hit.Highlight[TitleField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.New(hit.Source.Title);

        return ToSearchMatch(hit.Source.Title, highlight, hit.Score ?? 1.0);
    }

    public static SearchMatch GetSearchMatch(this IHit<IndexedPlaceContact> hit)
    {
        var highlight = hit.Highlight[TitleField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.New(hit.Source.Title);

        return ToSearchMatch(hit.Source.Title, highlight, hit.Score ?? 1.0);
    }

    public static SearchMatch GetSearchMatch(this IHit<IndexedEntry> hit)
    {
        var highlight = hit.Highlight[ContentField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.New(hit.Source.Content);

        return ToSearchMatch(hit.Source.Content, highlight, hit.Score ?? 1.0);
    }

    public static SearchMatch ToSearchMatch(string plain, string highlight, double score)
    {
        var plainHighlight = highlight.OrdinalReplace(PreTag, "").OrdinalReplace(PostTag, "");
        var offset = 0;
        if (!OrdinalEquals(plain, plainHighlight)) {
            plainHighlight = ZString.Concat(SkippedPartReplacement, plainHighlight, SkippedPartReplacement);
            offset = SkippedPartReplacement.Length;
        }
        var searchMatchParts = FindRanges(highlight)
            .Select(x => new SearchMatchPart(x.Move(offset), 1))
            .ToArray();
        return new (plainHighlight, score, searchMatchParts);
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
