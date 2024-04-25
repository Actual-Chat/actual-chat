using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using ActualLab.Diagnostics;

namespace ActualChat.Contacts.UI.Blazor.Services;

public class ContactSync(UIHub hub) : ScopedWorkerBase<UIHub>(hub), IComputeService
{
    private const int BatchSize = 100;
    private static readonly RandomTimeSpan BatchInterval = TimeSpan.FromSeconds(1).ToRandom(0.1);

    private AccountUI? _accountUI;
    private IExternalContacts? _externalContacts;
    private IExternalContactHashes? _externalContactHashes;
    private DeviceContacts? _deviceContacts;
    private ExternalContactHasher? _externalContactHasher;
    private ContactsPermissionHandler? _contactsPermission;

    private AccountUI AccountUI => _accountUI ??= Services.GetRequiredService<AccountUI>();
    private IExternalContacts ExternalContacts
        => _externalContacts ??= Services.GetRequiredService<IExternalContacts>();
    private IExternalContactHashes ExternalContactHashes
        => _externalContactHashes ??= Services.GetRequiredService<IExternalContactHashes>();
    private ExternalContactHasher ExternalContactHasher
        => _externalContactHasher ??= Services.GetRequiredService<ExternalContactHasher>();
    private DeviceContacts DeviceContacts
        => _deviceContacts ??= Services.GetRequiredService<DeviceContacts>();
    private ContactsPermissionHandler ContactsPermission
        => _contactsPermission ??= Services.GetRequiredService<ContactsPermissionHandler>();

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(TrySync)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(60, 600), Log)
            .AppendDelay(TimeSpan.FromMinutes(60))
            .CycleForever()
            .RunIsolated(cancellationToken);

    private async Task TrySync(CancellationToken cancellationToken)
    {
        var deviceId = DeviceContacts.DeviceId;
        if (deviceId.IsEmpty) // True for non-MAUI apps
            return;

        var cAccount = await AccountUI.OwnAccount.When(x => !x.IsGuestOrNone, cancellationToken).ConfigureAwait(false);
        var account = cAccount.Value;
        var abortCts = cancellationToken.CreateLinkedTokenSource();
        var abortToken = abortCts.Token;
        Task whenSynced;
        try {
            whenSynced = Sync(account, abortToken);
            var whenSignedOut = cAccount.When(x => x.IsGuestOrNone || x.Id != account.Id, abortToken);
            await Task.WhenAny(whenSynced, whenSignedOut).ConfigureAwait(false);
        }
        finally {
            // No matter which task completes first, we abort both here
            abortCts.CancelAndDisposeSilently();
        }
        // And we anyway await for whenSynced to make sure
        // an exception is thrown in case it fails
        await whenSynced.ConfigureAwait(false);
    }

    private async Task Sync(AccountFull account, CancellationToken cancellationToken)
    {
        await ContactsPermission.Check(cancellationToken).ConfigureAwait(false);
        await ContactsPermission.Cached.When(x => x == true, cancellationToken).ConfigureAwait(false);

        var deviceContacts = await DeviceContacts.List(cancellationToken).ConfigureAwait(false);
        var deviceRootHash = ExternalContactHasher.Compute(deviceContacts);
        var existingRootHash = await ExternalContactHashes.Get(Session, DeviceContacts.DeviceId, cancellationToken).ConfigureAwait(false);
        if (deviceRootHash == existingRootHash?.Hash)
            return;

        var changes = await GetChanges(deviceContacts, cancellationToken).ConfigureAwait(false);
        await SaveChanges(changes, cancellationToken).ConfigureAwait(false);

        // NOTE(AY): SaveChanges may take a while - especially with the original 30-second
        // pause between batches. Ideally it makes sense to update the root hash on the server side -
        // right in the same transaction where you update the contacts.
        // The logic here implies that it's going to retry sync in case we somehow didn't get
        // to this point.

        var rootHash = existingRootHash ?? new ExternalContactsHash(new UserDeviceId(account.Id, DeviceContacts.DeviceId));
        rootHash = rootHash with { Hash = deviceRootHash };
        var changeHashCmd = new ExternalContactHashes_Change(Session,
            DeviceContacts.DeviceId,
            existingRootHash?.Version,
            Change.Upsert(rootHash));
        await Commander.Call(changeHashCmd, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<ExternalContactChange>> GetChanges(
        ApiArray<ExternalContactFull> deviceContacts,
        CancellationToken cancellationToken)
    {
        var existingContacts = await ExternalContacts
            .List(Session, DeviceContacts.DeviceId, cancellationToken)
            .ConfigureAwait(false);
        var existingMap = existingContacts.ToDictionary(x => x.Id);
        var toAdd = deviceContacts.Where(x => !existingMap.ContainsKey(x.Id)).ToList();
        var toRemove = existingMap.Keys.Except(deviceContacts.Select(x => x.Id)).ToList();
        var toUpdate = deviceContacts.Select(deviceContact => {
                if (!existingMap.TryGetValue(deviceContact.Id, out var existing))
                    return null;

                return existing.Hash == deviceContact.Hash
                    ? null
                    : deviceContact with { Version = existing.Version };
            })
            .SkipNullItems()
            .ToList();

        var changes = toRemove.Select(x => new ExternalContactChange(x, null, Change.Remove<ExternalContactFull>()))
            .Concat(ToChanges(toUpdate, Change.Update))
            .Concat(ToChanges(toAdd, Change.Create))
            .ToList();
        return changes;

        IEnumerable<ExternalContactChange> ToChanges(
            IEnumerable<ExternalContactFull> externalContacts,
            Func<ExternalContactFull, Change<ExternalContactFull>> changeFactory)
            => externalContacts.Select(x => new ExternalContactChange(x.Id, x.Version, changeFactory.Invoke(x)));
    }

    private async Task SaveChanges(
        IEnumerable<ExternalContactChange> changes,
        CancellationToken cancellationToken)
    {
        var batches = changes.Chunk(BatchSize).ToList();
        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++) {
            var batch = batches[batchIndex];
            if (batchIndex > 0)
                await Task.Delay(BatchInterval.Next(), cancellationToken).ConfigureAwait(false);
            try {
                var changeResults = await Commander
                    .Call(new ExternalContacts_BulkChange(Session, batch.ToApiArray()), cancellationToken)
                    .ConfigureAwait(false);
                var syncedCount = changeResults.Count(x => x.Error is null);
                var logLevel = syncedCount != batch.Length ? LogLevel.Warning : LogLevel.Debug;
                Log.IfEnabled(logLevel)?.Log(logLevel,
                    "Sync batch #{BatchIndex}/{BatchCount}: {SyncedCount}/{Count} contacts synced",
                    batchIndex + 1, batches.Count, syncedCount, batch.Length);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                Log.LogWarning(e,
                    "Sync batch #{BatchIndex}/{BatchCount} failed",
                    batchIndex + 1, batches.Count);
                throw;
            }
        }
    }
}
