using ActualChat.Search;

namespace ActualChat.MLSearch;

public static class EntrySearchQueryExt
{
    public static EntrySearchQuery Clamp(this EntrySearchQuery query)
        => query with {
            Skip = query.Skip.Clamp(0, int.MaxValue),
            Limit = query.Limit.Clamp(0, Constants.Search.PageSizeLimit),
        };

    public static List<(string Tag, bool IsPrefix)> GetHashtags(
        this EntrySearchQuery query,
        IMarkupParser markupParser)
    {
        // Parsing the criteria with the message grammar keeps the query and the message in sync:
        // "#a#b", "#4121" and a tag inside a code span aren't tags here either. IsPrefix mirrors
        // MatchBoolPrefix on Content - only a tag ending the criteria may still be half-typed.
        var criteria = query.Criteria;
        var tags = HashtagExtractor.Instance.GetTags(markupParser.Parse(criteria));
        if (tags.Count == 0)
            return [];

        var trailingTag = GetTrailingTag(criteria, tags);
        return tags.Select(x => (x, x == trailingTag)).ToList();
    }

    // Private methods

    private static string GetTrailingTag(string criteria, HashSet<string> tags)
    {
        var start = criteria.Length;
        while (start > 0 && !char.IsWhiteSpace(criteria[start - 1]))
            start--;

        if (start == criteria.Length)
            return ""; // The criteria ends with whitespace, so its last tag is complete

        var lastToken = criteria[start..];
        if (lastToken.Length < 2 || lastToken[0] != '#')
            return "";

        var tag = lastToken[1..].ToLower();
        return tags.Contains(tag) ? tag : "";
    }
}
