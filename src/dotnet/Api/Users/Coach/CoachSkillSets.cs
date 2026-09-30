namespace ActualChat.Users;

public static class CoachSkillSets
{
    public static readonly CoachMetricKind[] Conversation = [
        CoachMetricKind.TurnTaking, CoachMetricKind.Monologue, CoachMetricKind.Interruptions,
        CoachMetricKind.Patience, CoachMetricKind.Pauses,
    ];

    public static CoachMetricKind[] Headline(CoachLanguageLevel level)
        => level == CoachLanguageLevel.Learning
            ? [
                CoachMetricKind.WeakWords,
                CoachMetricKind.Vocabulary,
                CoachMetricKind.SentenceLength,
                CoachMetricKind.Pace,
            ]
            : [CoachMetricKind.Fillers, CoachMetricKind.Pace, CoachMetricKind.TurnTaking, CoachMetricKind.Monologue];

    public static bool IsLanguageBound(CoachMetricKind kind)
        => !Conversation.Contains(kind);
}
