using System.Text.RegularExpressions;
using ActualChat.MLSearch.Documents;
using ActualChat.Search;
using OpenSearch.Client;
using IndexedEntry = ActualChat.MLSearch.Documents.IndexedEntry;

namespace ActualChat.MLSearch.Engine.OpenSearch.Extensions;

public static partial class HighlightsConverter
{
    public const string PreTag = @"⫷⩧";
    public const string PostTag = @"⩧⫸";
    public const string SkippedPartReplacement = "…";
    private static readonly string AccountNameField = nameof(IndexedUser.Name).Decapitalize();
    private static readonly string ContactNameField = nameof(IndexedUserContact.Name).Decapitalize();
    private static readonly string ExternalContactNameField = nameof(IndexedUserContact.ExternalContactName).Decapitalize();
    private static readonly string UserRelationName = nameof(IndexedUser).ToLower();
    private static readonly string TitleField = nameof(Chat.Chat.Title).Decapitalize();
    private static readonly string ContentField = nameof(ChatEntry.Content).Decapitalize();

    [GeneratedRegex(@"[\s^\u200B]+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegexFactory();

    private static readonly Regex WordRegex = SpaceRegexFactory();

    public static SearchMatch GetSearchMatch(this IHit<IndexedUser> hit)
    {
        var highlight = hit.Highlight[AccountNameField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.Matchless(hit.Source.Name);

        return ToSearchMatch(hit.Source.Name, highlight, hit.Score ?? 1.0);
    }

    public static SearchMatch GetSearchMatch(this IHit<IndexedUserContact> hit)
    {
        var (name, highlight) = GetHighlight();
        return highlight.IsNullOrEmpty()
            ? SearchMatch.Matchless(hit.Source.Name)
            : ToSearchMatch(name, highlight, hit.Score ?? 1.0);

        (string Name, string? Higlight) GetHighlight()
        {
            if (hit.Highlight.TryGetValue(ContactNameField, out var highlights))
                return (hit.Source.Name, highlights.FirstOrDefault(x => !x.IsNullOrEmpty()));

            if (hit.Highlight.TryGetValue(ExternalContactNameField, out highlights))
                return (hit.Source.ExternalContactName, highlights.FirstOrDefault(x => !x.IsNullOrEmpty()));

            foreach (var userHit in hit.InnerHits[UserRelationName].Hits.Hits)
                if (userHit.Highlight.TryGetValue(AccountNameField, out highlights))
                    return (userHit.Source.As<IndexedUser>().Name, highlights.FirstOrDefault(x => !x.IsNullOrEmpty()));

            return (hit.Source.Name, null);
        }
    }

    public static SearchMatch GetSearchMatch(this IHit<IndexedGroup> hit)
    {
        var highlight = hit.Highlight[TitleField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.Matchless(hit.Source.Title);

        return ToSearchMatch(hit.Source.Title, highlight, hit.Score ?? 1.0);
    }

    public static SearchMatch GetSearchMatch(this IHit<IndexedPlace> hit)
    {
        var highlight = hit.Highlight[TitleField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.Matchless(hit.Source.Title);

        return ToSearchMatch(hit.Source.Title, highlight, hit.Score ?? 1.0);
    }

    public static SearchMatch GetSearchMatch(this IHit<IndexedEntry> hit)
    {
        var highlight = hit.Highlight[ContentField].FirstOrDefault(x => !x.IsNullOrEmpty());
        if (highlight.IsNullOrEmpty())
            return SearchMatch.Matchless(hit.Source.Content);

        return ToSearchMatch(hit.Source.Content, highlight, hit.Score ?? 1.0);
    }

    public static IEnumerable<string> GetHighlightedWords(this IHit<IndexedEntry> hit)
    {
        var highlights = hit.Highlight[ContentField].Where(x => !x.IsNullOrEmpty()).ToList();
        return highlights.Count == 0 ? [] : highlights.SelectMany(GetHighlightedWords);
    }

    public static IEnumerable<string> GetHighlightedWords(string highlight)
    {
        var position = 0;
        while (position <= highlight.Length) {
            var iStart = highlight.IndexOf(PreTag, position);
            if (iStart < 0)
                yield break;

            iStart += PreTag.Length;
            var iEnd = highlight.IndexOf(PostTag, iStart);
            if (iEnd < 0)
                yield break;

            foreach (var word in WordRegex.Split(highlight[iStart..iEnd]))
                yield return word.ToLower();

            position = iEnd + PostTag.Length;
        }
    }

    public static SearchMatch ToSearchMatch(string plain, string highlight, double score)
    {
        var plainHighlight = highlight.Replace(PreTag, "").Replace(PostTag, "");
        var offset = 0;
        if (plain != plainHighlight) {
            plainHighlight = string.Concat(SkippedPartReplacement, plainHighlight, SkippedPartReplacement);
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
            var iStart = highlightedString.IndexOf(PreTag, position);
            if (iStart < 0)
                yield break;

            iStart += PreTag.Length;
            var iEnd = highlightedString.IndexOf(PostTag, iStart);
            if (iEnd < 0)
                yield break;

            position = iEnd + PostTag.Length;
            plainOffset += PreTag.Length;
            yield return new (iStart - plainOffset, iEnd - plainOffset);

            plainOffset += PostTag.Length;
        }
    }
}
