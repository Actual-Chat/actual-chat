namespace ActualChat.Chat;

/// <summary>
/// Pure tokenize / filter / rank functions used by <c>MentionIndexUI</c>. Kept side-effect-free
/// so it can be unit-tested without DI or fusion plumbing.
/// </summary>
public static class MentionFilter
{
    private static readonly char[] Separators = [
        ' ', '\t', '\r', '\n',
        '-', '_', '.', ',', ':', ';', '/', '\\', '|',
        '(', ')', '[', ']', '{', '}',
        '!', '?', '\'', '"', '`',
    ];

    /// <summary>
    /// Splits <paramref name="text"/> into lowercase words by whitespace and punctuation
    /// (see <c>Separators</c>). Empty results are dropped.
    /// </summary>
    public static string[] Tokenize(string? text)
    {
        if (text.IsNullOrEmpty())
            return [];

        var parts = text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        return parts;
    }

    /// <summary>
    /// True iff every token in <paramref name="queryTokens"/> is a prefix of some word
    /// in <paramref name="candidateWords"/>. Both inputs are expected lowercase.
    /// </summary>
    public static bool MatchesAll(string[] queryTokens, string[] candidateWords)
    {
        foreach (var q in queryTokens) {
            var hit = false;
            foreach (var w in candidateWords) {
                if (w.Length < q.Length)
                    continue;
                if (w.AsSpan(0, q.Length).SequenceEqual(q.AsSpan())) {
                    hit = true;
                    break;
                }
            }
            if (!hit)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Coverage score: total length of words hit by query prefixes, normalized by the
    /// candidate's total word length. Range [0, 1]. Higher = better.
    /// </summary>
    public static double CoverageScore(string[] queryTokens, string[] candidateWords)
    {
        if (queryTokens.Length == 0 || candidateWords.Length == 0)
            return 0;

        var hitChars = 0;
        var totalChars = 0;
        foreach (var w in candidateWords)
            totalChars += w.Length;
        if (totalChars == 0)
            return 0;

        foreach (var q in queryTokens) {
            var bestHit = 0;
            foreach (var w in candidateWords) {
                if (w.Length < q.Length)
                    continue;
                if (!w.AsSpan(0, q.Length).SequenceEqual(q.AsSpan()))
                    continue;
                if (q.Length > bestHit)
                    bestHit = q.Length;
            }
            hitChars += bestHit;
        }
        return (double)hitChars / totalChars;
    }

    /// <summary>
    /// Filters and ranks candidates per the spec:
    ///   1. drop candidates failing <see cref="MatchesAll"/>;
    ///   2. order by kind (User &lt; Chat &lt; Emoji), then chat-membership (members first),
    ///      then coverage descending, then alphabetical primary name;
    ///   3. take top <paramref name="limit"/>.
    /// </summary>
    public static MentionCandidate[] FilterAndRank(
        IReadOnlyList<MentionCandidate> pool,
        string query,
        MentionKindFilter kindFilter,
        int limit)
    {
        var tokens = Tokenize(query);
        if (limit <= 0)
            return [];

        var matched = new List<(MentionCandidate Candidate, double Score)>();
        foreach (var c in pool) {
            if (!kindFilter.Allows(c.Kind))
                continue;
            if (tokens.Length > 0 && !MatchesAll(tokens, c.Words))
                continue;
            var score = tokens.Length == 0 ? 0 : CoverageScore(tokens, c.Words);
            matched.Add((c, score));
        }

        matched.Sort(static (a, b) => {
            var kindCmp = a.Candidate.Kind.CompareTo(b.Candidate.Kind);
            if (kindCmp != 0) return kindCmp;
            var memberCmp = b.Candidate.IsChatMember.CompareTo(a.Candidate.IsChatMember);
            if (memberCmp != 0) return memberCmp;
            var scoreCmp = b.Score.CompareTo(a.Score);
            if (scoreCmp != 0) return scoreCmp;
            return string.CompareOrdinal(a.Candidate.PrimaryName, b.Candidate.PrimaryName);
        });

        var take = Math.Min(limit, matched.Count);
        var result = new MentionCandidate[take];
        for (var i = 0; i < take; i++)
            result[i] = matched[i].Candidate;
        return result;
    }
}
