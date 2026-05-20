namespace ActualChat.Chat;

/// <summary>
/// Pure normalize / match / rank functions used by <c>MentionIndexUI</c>. Kept side-effect-free
/// so it can be unit-tested without DI or fusion plumbing.
/// </summary>
public static class MentionFilter
{
    private static readonly char[] Separators = [
        ' ', '\t', '\r', '\n',
        '-', '_', '.', ',', ':', ';', '/', '\\', '|',
        '(', ')', '[', ']', '{', '}',
        '!', '?', '\'', '"', '`',
        '›', // › — place / chat separator
    ];

    /// <summary>
    /// Builds the normalized search blob: every token from <paramref name="parts"/> lowercased
    /// and prefixed with a space, e.g. ("Fusion Place", "Funny Chat") → " fusion place funny chat".
    /// A leading space before every token lets a prefix probe (see <see cref="GetPrefixes"/>)
    /// match word starts only.
    /// </summary>
    public static string Normalize(params string?[] parts)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        foreach (var part in parts) {
            if (part.IsNullOrEmpty())
                continue;
            var tokens = part.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens) {
                sb.Append(' ');
                sb.Append(token);
            }
        }
        return sb.ToStringAndRelease();
    }

    /// <summary>
    /// Builds query prefixes — each query token lowercased and space-prefixed so a candidate's
    /// <see cref="MentionCandidate.SearchText"/> can be probed with a single Ordinal Contains.
    /// </summary>
    public static string[] GetPrefixes(string? query)
    {
        if (query.IsNullOrEmpty())
            return [];

        var tokens = query.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return [];

        var result = new string[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            result[i] = ' ' + tokens[i];
        return result;
    }

    /// <summary>
    /// True iff every prefix occurs in <paramref name="searchText"/>. Because each prefix and
    /// every token in the search text start with a space, this matches word prefixes only.
    /// </summary>
    public static bool Matches(string searchText, string[] prefixes)
    {
        foreach (var prefix in prefixes) {
            if (!searchText.Contains(prefix, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Coverage score: total length of the matched prefixes over the candidate's total token
    /// length. Range [0, 1], higher = better. Assumes every prefix already matched.
    /// </summary>
    public static double CoverageScore(string searchText, string[] prefixes)
    {
        if (prefixes.Length == 0 || searchText.Length == 0)
            return 0;

        var totalChars = 0;
        foreach (var c in searchText) {
            if (c != ' ')
                totalChars++;
        }
        if (totalChars == 0)
            return 0;

        var hitChars = 0;
        foreach (var prefix in prefixes)
            hitChars += prefix.Length - 1; // minus the leading space
        return (double)hitChars / totalChars;
    }

    /// <summary>
    /// Filters and ranks candidates per the spec:
    ///   1. drop candidates failing <see cref="Matches"/>;
    ///   2. order by kind (User &lt; Chat &lt; Emoji), then chat-membership (members first),
    ///      then coverage descending, then alphabetical title;
    ///   3. take top <paramref name="limit"/>.
    /// </summary>
    public static MentionCandidate[] FilterAndRank(
        IReadOnlyList<MentionCandidate> pool,
        string query,
        MentionKindFilter kindFilter,
        int limit)
    {
        if (limit <= 0)
            return [];

        var prefixes = GetPrefixes(query);

        var matched = new List<(MentionCandidate Candidate, double Score)>();
        foreach (var c in pool) {
            if (!kindFilter.Allows(c.Kind))
                continue;
            if (prefixes.Length > 0 && !Matches(c.SearchText, prefixes))
                continue;
            var score = prefixes.Length == 0 ? 0 : CoverageScore(c.SearchText, prefixes);
            matched.Add((c, score));
        }

        matched.Sort(static (a, b) => {
            var kindCmp = a.Candidate.Kind.CompareTo(b.Candidate.Kind);
            if (kindCmp != 0) return kindCmp;
            var memberCmp = b.Candidate.IsChatMember.CompareTo(a.Candidate.IsChatMember);
            if (memberCmp != 0) return memberCmp;
            var scoreCmp = b.Score.CompareTo(a.Score);
            if (scoreCmp != 0) return scoreCmp;
            return string.CompareOrdinal(a.Candidate.Title, b.Candidate.Title);
        });

        var take = Math.Min(limit, matched.Count);
        var result = new MentionCandidate[take];
        for (var i = 0; i < take; i++)
            result[i] = matched[i].Candidate;
        return result;
    }
}
