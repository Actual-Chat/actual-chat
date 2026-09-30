using ActualChat.Users.Module;

namespace ActualChat.Users;

public static class CoachFocus
{
    private static readonly CoachBand[] WorstFirst = [CoachBand.High, CoachBand.Low, CoachBand.Medium, CoachBand.Good];

    public static CoachMetricKind? Pick(CoachSummary summary, CoachLanguageLevel level, CoachScoringSettings s)
    {
        if (summary.Words < s.MinScoreWords)
            return null;

        var headline = CoachSkillSets.Headline(level);
        foreach (var band in WorstFirst)
            foreach (var kind in headline)
                if (summary.Metrics.Any(m => m.Kind == kind && m.Band == band))
                    return kind;
        return null;
    }
}
