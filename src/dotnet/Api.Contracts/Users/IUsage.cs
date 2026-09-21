namespace ActualChat.Users;

/// <summary>
/// The user's own usage numbers and the review-prompt decision built on them.
/// </summary>
public interface IUsage : IComputeService
{
    [ComputeMethod]
    Task<UsageSummary> GetOwnSummary(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AppReviewPromptState> GetOwnReviewPromptHistory(Session session, CancellationToken cancellationToken);
    // The prompt the server decided on after a live session, while it is still fresh; the app shows it
    [ComputeMethod]
    Task<PendingReviewPrompt?> GetPendingReviewPrompt(Session session, CancellationToken cancellationToken);
    // Not a compute method on purpose: the summary behind it changes on every recorded entry
    Task<ReviewPromptState> GetReviewPromptState(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRecordReviewPrompt(Usage_RecordReviewPrompt command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnResetReviewPrompt(Usage_ResetReviewPrompt command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRebuildOwnDays(Usage_RebuildOwnDays command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Usage_RecordReviewPrompt : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ReviewPromptOutcome Outcome { get; init; }
}

/// <summary>
/// Admin-only, for the test page: forgets the caller's prompt history.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Usage_ResetReviewPrompt : ApiCommand<Unit>;

/// <summary>
/// Admin-only, for the test page: recomputes the caller's day rows from the event log.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Usage_RebuildOwnDays : ApiCommand<Unit>;
