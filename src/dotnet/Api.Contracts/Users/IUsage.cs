namespace ActualChat.Users;

/// <summary>
/// The user's own usage numbers.
/// </summary>
public interface IUsage : IComputeService
{
    [ComputeMethod]
    Task<UsageSummary> GetOwnSummary(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRebuildOwnDays(Usage_RebuildOwnDays command, CancellationToken cancellationToken);
}

/// <summary>
/// Admin-only, for the test page: recomputes the caller's day rows from the event log.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Usage_RebuildOwnDays : ApiCommand<Unit>;
