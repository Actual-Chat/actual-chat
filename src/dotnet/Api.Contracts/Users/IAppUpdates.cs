namespace ActualChat.Users;

/// <summary>
/// Reports the newest build known to be published in the store for a given app kind,
/// so a client running an older build can offer an update.
/// </summary>
public interface IAppUpdates : IComputeService
{
    // null means "unknown" - the answer on non-production instances and until the first check lands.
    // Consolidation keeps a re-check that found nothing from reaching clients as a fake update.
    [ComputeMethod(ConsolidationDelay = 0)]
    Task<AppUpdateInfo?> GetLatestUpdateInfo(AppKind appKind, CancellationToken cancellationToken);
}
