using ActualChat.Search;

namespace ActualChat.Chat;

/// <summary>
/// Category predicates the mention picker narrows candidates with, plus the kind-group
/// ordering and the <see cref="FilterAndRank"/> entry point. Each predicate keys off
/// <see cref="MentionCandidate.Id"/>'s <see cref="MentionRef.Kind"/>.
/// </summary>
public static class MentionCandidateFilters
{
    public static readonly Func<MentionCandidate, bool> All = static _ => true;
    public static readonly Func<MentionCandidate, bool> User =
        static c => c.Id.Kind == MentionKind.User || c.Id.Kind == MentionKind.Author;
    public static readonly Func<MentionCandidate, bool> Chat =
        static c => c.Id.Kind == MentionKind.Chat || c.Id.Kind == MentionKind.Place;
    public static readonly Func<MentionCandidate, bool> Emoji =
        static c => c.Id.Kind == MentionKind.Emoji;

    private static readonly Func<MentionCandidate, bool>[] RankOrder = [User, Chat, Emoji];

    // How much a recent-mention's recency score adds to its coverage score on a typed query.
    // Additive (not absolute): a clearly better text match can still outrank a recent one.
    private const double RecencyBoostWeight = 0.3;

    public static ApiArray<MentionCandidate> FilterAndRank(
        this ApiArray<MentionCandidate> candidates,
        Func<MentionCandidate, bool> filter,
        string query,
        int limit,
        IReadOnlyDictionary<MentionRef, double>? recencyScores = null)
    {
        if (limit <= 0)
            return [];

        var searchQuery = new SearchQuery(query);
        var matched = new List<(MentionCandidate Candidate, double Coverage, double Recency)>();
        foreach (var c in candidates) {
            if (!filter.Invoke(c))
                continue;
            if (!searchQuery.IsEmpty && !c.SearchDocument.IsMatch(searchQuery))
                continue;

            var recency = recencyScores != null && recencyScores.TryGetValue(c.Id, out var r) ? r : 0d;
            matched.Add((c, c.SearchDocument.GetCoverageScore(searchQuery), recency));
        }

        if (searchQuery.IsEmpty)
            // Default view (just "@"): recents first (most relevant first), then the rest by kind.
            matched.Sort(static (a, b) => {
                var recencyCmp = b.Recency.CompareTo(a.Recency);
                if (recencyCmp != 0)
                    return recencyCmp;

                return CompareByKind(a, b);
            });
        else
            // Typed query: kind, membership, then coverage with an additive recency boost.
            matched.Sort(static (a, b) => {
                var kindCmp = GetKindBasedRank(a.Candidate).CompareTo(GetKindBasedRank(b.Candidate));
                if (kindCmp != 0)
                    return kindCmp;

                var memberCmp = b.Candidate.IsChatMember.CompareTo(a.Candidate.IsChatMember);
                if (memberCmp != 0)
                    return memberCmp;

                var scoreCmp = (b.Coverage + RecencyBoostWeight * b.Recency)
                    .CompareTo(a.Coverage + RecencyBoostWeight * a.Recency);
                if (scoreCmp != 0)
                    return scoreCmp;

                return string.CompareOrdinal(a.Candidate.Title, b.Candidate.Title);
            });

        var take = Math.Min(limit, matched.Count);
        var result = new MentionCandidate[take];
        for (var i = 0; i < take; i++)
            result[i] = matched[i].Candidate;
        return result.ToApiArray(makeCopy: false);
    }

    // Private methods

    private static int CompareByKind(
        (MentionCandidate Candidate, double Coverage, double Recency) a,
        (MentionCandidate Candidate, double Coverage, double Recency) b)
    {
        var kindCmp = GetKindBasedRank(a.Candidate).CompareTo(GetKindBasedRank(b.Candidate));
        if (kindCmp != 0)
            return kindCmp;

        var memberCmp = b.Candidate.IsChatMember.CompareTo(a.Candidate.IsChatMember);
        if (memberCmp != 0)
            return memberCmp;

        return string.CompareOrdinal(a.Candidate.Title, b.Candidate.Title);
    }

    private static int GetKindBasedRank(MentionCandidate candidate)
    {
        for (var i = 0; i < RankOrder.Length; i++) {
            if (RankOrder[i].Invoke(candidate))
                return i;
        }
        return RankOrder.Length;
    }
}
