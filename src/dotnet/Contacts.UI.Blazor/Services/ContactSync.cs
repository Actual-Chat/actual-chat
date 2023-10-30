using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Contacts.UI.Blazor.Services;

public class ContactSync(IServiceProvider services) : WorkerBase, IComputeService
{
    private static readonly TimeSpan ThrottlingInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 100;
    private Session Session { get; } = services.Session();
    private AccountUI AccountUI { get; } = services.GetRequiredService<AccountUI>();
    private IExternalContacts ExternalContacts { get; } = services.GetRequiredService<IExternalContacts>();
    private DeviceContacts DeviceContacts { get; } = services.GetRequiredService<DeviceContacts>();
    private ContactsPermissionHandler ContactsPermission { get; } = services.GetRequiredService<ContactsPermissionHandler>();
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();
    private ICommander Commander { get; } = services.Commander();
    private ILogger Log { get; } = services.LogFor<ContactSync>();

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChainExt.From(SyncUntilSignedOut),
        };
        var retryDelays = RetryDelaySeq.Exp(3, 600);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task SyncUntilSignedOut(CancellationToken cancellationToken)
    {
        var deviceId = DeviceContacts.DeviceId;
        if (deviceId.IsEmpty)
            return;

        var isCompleted = false;
        while (!isCompleted && !cancellationToken.IsCancellationRequested) {
            using var cts = cancellationToken.CreateLinkedTokenSource();
            try {
                var cAccount = await WhenAuthenticated(cancellationToken).ConfigureAwait(false);
                var whenSignedOut = cAccount.When(x => x.IsGuestOrNone || x.Id != cAccount.Value.Id, cts.Token);
                var whenSynced = Sync(cts.Token);
                await Task.WhenAny(whenSynced, whenSignedOut).ConfigureAwait(false);
                isCompleted = whenSynced.IsCompletedSuccessfully;
                cts.Cancel();
            }
            catch (OperationCanceledException) { }
        }
    }

    private async Task Sync(CancellationToken cancellationToken)
    {
        await ContactsPermission.Check(cancellationToken).ConfigureAwait(false);
        await ContactsPermission.Cached.When(x => x == true, cancellationToken).ConfigureAwait(false);
        var existingContacts = await ExternalContacts.List(Session, DeviceContacts.DeviceId, cancellationToken).ConfigureAwait(false);
        var existingMap = existingContacts.ToDictionary(x => x.Id);
        var deviceContacts = await DeviceContacts.List(cancellationToken).ConfigureAwait(false);

        var toAdd = deviceContacts.Where(x => !existingMap.ContainsKey(x.Id)).ToList();
        var toRemove = existingContacts.ExceptBy(deviceContacts.Select(x => x.Id), x => x.Id).ToList();
        var toUpdate = deviceContacts.Select(x => {
                if (!existingMap.TryGetValue(x.Id, out var externalContact))
                    return null;

                var diff = DiffEngine.Diff<ExternalContact, ExternalContactDiff>(x, externalContact);
                return diff == ExternalContactDiff.Empty ? null : DiffEngine.Patch(externalContact, diff);
            })
            .SkipNullItems()
            .ToList();

        var changes = ToChanges(toRemove, Change.Remove)
            .Concat(ToChanges(toUpdate, Change.Update))
            .Concat(ToChanges(toAdd, Change.Create))
            .ToList();

        foreach (var bulk in changes.Chunk(BatchSize)) {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var changeResults = await Commander
                    .Call(new ExternalContacts_BulkChange(Session, bulk.ToApiArray()), cancellationToken)
                    .ConfigureAwait(false);
                var succeedCount = changeResults.Count(x => x.Error is null);
                if (bulk.Length > succeedCount)
                    Log.LogWarning("Synced {SucceedCount} of {Count} contacts", succeedCount, bulk.Length);
                else
                    Log.LogDebug("Synced {Count} contacts", succeedCount);
                await Task.Delay(ThrottlingInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception e)
            {
                Log.LogWarning(e, "Failed to sync {Count} contacts", bulk.Length);
            }
        }
        return;

        IEnumerable<ExternalContactChange> ToChanges(
            IEnumerable<ExternalContact> externalContacts,
            Func<ExternalContact, Change<ExternalContact>> toChange)
            => externalContacts.Select(x => new ExternalContactChange(x.Id, x.Version, toChange(x)));
    }

    private Task<Computed<AccountFull>> WhenAuthenticated(CancellationToken cancellationToken)
        => AccountUI.OwnAccount.When(x => x.IsActive(), cancellationToken);
}
