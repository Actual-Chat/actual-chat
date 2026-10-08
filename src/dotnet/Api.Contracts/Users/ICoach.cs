namespace ActualChat.Users;

/// <summary>
/// The caller's own speech-coach numbers: the scored window, the day series, the pending live tip
/// and the occurrences behind jump-to-audio. Nothing here reaches another user's rows.
/// </summary>
public interface ICoach : IComputeService
{
    public const int MaxOccurrences = CoachOccurrence.MaxCount;

    // The per-user verdict: the chat-side master switch and the rollout rule
    [ComputeMethod]
    Task<bool> IsEnabled(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<CoachSummary> GetOwnSummary(
        Session session, CoachWindow window, string? language, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<CoachDay>> ListOwnDays(
        Session session, Range<Moment> dayRange, string? language, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<CoachScorePart>> ExplainOwnScore(
        Session session, string? language, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<CoachConversation>> ListOwnConversations(
        Session session, int count, string? language, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<UserCoachTip?> GetPendingTip(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<CoachOccurrence>> ListOwnOccurrences(
        Session session, string word, CoachWindow window, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<CoachOccurrence>> ListOwnSkillOccurrences(
        Session session, string word, CoachWindow window,
        string? language, CoachMetricKind kind, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<CoachChip>> ListOwnSkillWords(
        Session session, CoachWindow window, string? language, CoachMetricKind kind,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<CoachPaceDetails> GetOwnPaceDetails(
        Session session, CoachWindow window, string language, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<CoachSkillHistory> GetOwnSkillHistory(
        Session session, CoachMetricKind kind, string language, CoachHistoryPeriod period,
        Moment anchor, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<CoachOccurrence>> ListOwnSkillOccurrencesInRange(
        Session session, string word, Range<Moment> range, string language,
        CoachMetricKind kind, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<CoachBaselineComparison> GetOwnBaselineComparison(
        Session session, CoachMetricKind kind, string language, CoachHistoryPeriod period,
        Moment anchor, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<CoachMetricKind?> GetOwnFocus(Session session, string? language, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<CoachWeekDelta>> GetOwnWeekDeltas(
        Session session, string? language, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<CoachMilestone>> ListOwnMilestones(Session session, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<CoachWeekScore>> ListOwnWeekScores(
        Session session, int weeks, string? language, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<CoachLanguageInfo>> ListOwnLanguages(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSetBaseline(Coach_SetBaseline command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetFocus(Coach_SetFocus command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetLanguageLevel(Coach_SetLanguageLevel command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetChatCoaching(Coach_SetChatCoaching command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnExcludeConversation(Coach_ExcludeConversation command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDeleteOwnData(Coach_DeleteOwnData command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDismissTip(Coach_DismissTip command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRebuildOwnDays(Coach_RebuildOwnDays command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
public sealed partial record Coach_SetBaseline : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Language { get; init; }
    [DataMember(Order = 3), Key(3)] public required CoachMetricKind Kind { get; init; }
    [DataMember(Order = 4), Key(4)] public CoachHistoryPeriod? Period { get; init; }
    [DataMember(Order = 5), Key(5)] public Moment Anchor { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Coach_DismissTip : ApiCommand<Unit>;

/// <summary>
/// Admin-only: recomputes the caller's day rows from the log.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Coach_RebuildOwnDays : ApiCommand<Unit>;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Coach_SetFocus : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Language { get; init; }
    // null = automatic focus
    [DataMember(Order = 3), Key(3)] public CoachMetricKind? Kind { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Coach_SetLanguageLevel : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Language { get; init; }
    [DataMember(Order = 3), Key(3)] public required CoachLanguageLevel Level { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Coach_SetChatCoaching : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    // null = inherit from the place, then from the user's coach settings
    [DataMember(Order = 3), Key(3)] public bool? IsEnabled { get; init; }
}

/// <summary>
/// Takes one conversation out of the caller's scores, progress and tips, or puts it back; the
/// conversation stays listed either way.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Coach_ExcludeConversation : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long StartEntryLid { get; init; }
    [DataMember(Order = 4), Key(4)] public required string Language { get; init; }
    [DataMember(Order = 5), Key(5)] public required bool IsExcluded { get; init; }
}

/// <summary>
/// Removes every coaching row, tip and note of the caller; messages stay.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Coach_DeleteOwnData : ApiCommand<Unit>;
