namespace ActualChat.Users;

/// <summary>
/// The caller's own speech-coach numbers: the scored window, the day series, the pending live tip
/// and the occurrences behind jump-to-audio. Nothing here reaches another user's rows.
/// </summary>
public interface ICoach : IComputeService
{
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
    Task<ApiArray<CoachConversation>> ListOwnConversations(
        Session session, int count, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<UserCoachTip?> GetPendingTip(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<CoachOccurrence>> ListOwnOccurrences(
        Session session, string word, CoachWindow window, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnDismissTip(Coach_DismissTip command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRebuildOwnDays(Coach_RebuildOwnDays command, CancellationToken cancellationToken);
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
