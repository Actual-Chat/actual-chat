namespace ActualChat.Chat.Coach;

/// <summary>
/// Turn-taking numbers for one author over one conversation, from entry timings only.
/// </summary>
public sealed record ConversationStats(
    double OwnSpeechSeconds,
    double TotalSpeechSeconds,
    int OwnTurns,
    int TotalTurns,
    int Participants,
    double LongestMonologueSeconds,
    int Responses,
    double ResponseGapSeconds,
    int Interruptions)
{
    public static ConversationStats? Compute(
        IReadOnlyList<ChatEntry> entries, AuthorId authorId, double maxResponseGapSeconds)
    {
        var voice = entries
            .Where(e => e is { HasAudio: true, IsRemoved: false, EndsAt: not null })
            .OrderBy(e => e.LocalId)
            .ToList();
        if (voice.All(e => e.AuthorId != authorId))
            return null;

        var ownSpeech = 0d;
        var totalSpeech = 0d;
        var ownTurns = 0;
        var totalTurns = 0;
        var longestMonologue = 0d;
        var responses = 0;
        var responseGap = 0d;
        var interruptions = 0;
        var authors = new HashSet<AuthorId>();

        AuthorId? turnAuthor = null;
        Moment turnStart = default;
        Moment turnEnd = default;
        ChatEntry? previousOther = null;
        foreach (var e in voice) {
            var endsAt = e.EndsAt!.Value;
            var duration = (endsAt - e.BeginsAt).TotalSeconds;
            totalSpeech += duration;
            authors.Add(e.AuthorId);
            var isOwn = e.AuthorId == authorId;
            if (isOwn)
                ownSpeech += duration;

            if (e.AuthorId != turnAuthor) {
                CloseTurn();
                turnAuthor = e.AuthorId;
                turnStart = e.BeginsAt;
                totalTurns++;
                if (isOwn) {
                    ownTurns++;
                    if (previousOther is not null) {
                        var gap = (e.BeginsAt - previousOther.EndsAt!.Value).TotalSeconds;
                        if (gap < 0)
                            interruptions++;
                        else if (gap <= maxResponseGapSeconds) {
                            responses++;
                            responseGap += gap;
                        }
                    }
                }
            }
            turnEnd = endsAt;
            if (!isOwn)
                previousOther = e;
        }
        CloseTurn();

        return new ConversationStats(
            ownSpeech,
            totalSpeech,
            ownTurns,
            totalTurns,
            authors.Count,
            longestMonologue,
            responses,
            responses > 0 ? responseGap / responses : 0,
            interruptions);

        void CloseTurn() {
            if (turnAuthor == authorId)
                longestMonologue = Math.Max(longestMonologue, (turnEnd - turnStart).TotalSeconds);
        }
    }
}
