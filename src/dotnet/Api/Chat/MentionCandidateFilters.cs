namespace ActualChat.Chat;

/// <summary>
/// Category predicates the mention picker narrows candidates with, plus the kind-group
/// ordering and the <see cref="FilterAndRank"/> entry point. Each predicate keys off
/// <see cref="MentionCandidate.Id"/>'s <see cref="MentionId.Kind"/>.
/// </summary>
public static class MentionCandidateFilters
{
    public static readonly Func<MentionCandidate, bool> All = _ => true;
    public static readonly Func<MentionCandidate, bool> User =
        static c => c.Id.Kind == MentionKind.User || c.Id.Kind == MentionKind.Author;
    public static readonly Func<MentionCandidate, bool> Chat =
        static c => c.Id.Kind == MentionKind.Chat || c.Id.Kind == MentionKind.Place;
    public static readonly Func<MentionCandidate, bool> Emoji =
        static c => c.Id.Kind == MentionKind.Emoji;

    private static readonly Func<MentionCandidate, bool>[] RankOrder = [User, Chat, Emoji];

    public static ApiArray<MentionCandidate> FilterAndRank(
        this ApiArray<MentionCandidate> candidates,
        Func<MentionCandidate, bool> filter,
        string query,
        int limit)
    {
        if (limit <= 0)
            return [];

        var searchQuery = new MemSearchQuery(query);
        var matched = new List<(MentionCandidate Candidate, double Score)>();
        foreach (var c in candidates) {
            if (!filter.Invoke(c))
                continue;
            if (!searchQuery.IsEmpty && !c.MemSearchDocument.IsMatch(searchQuery))
                continue;

            matched.Add((c, c.MemSearchDocument.GetCoverageScore(searchQuery)));
        }

        // Order: kind (User < Chat < Emoji), chat-membership, coverage desc, alphabetical title.
        matched.Sort(static (a, b) => {
            var kindCmp = GetKindBasedRank(a.Candidate).CompareTo(GetKindBasedRank(b.Candidate));
            if (kindCmp != 0)
                return kindCmp;

            var memberCmp = b.Candidate.IsChatMember.CompareTo(a.Candidate.IsChatMember);
            if (memberCmp != 0)
                return memberCmp;

            var scoreCmp = b.Score.CompareTo(a.Score);
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

    private static int GetKindBasedRank(MentionCandidate candidate)
    {
        for (var i = 0; i < RankOrder.Length; i++) {
            if (RankOrder[i].Invoke(candidate))
                return i;
        }
        return RankOrder.Length;
    }
}
