using ActualChat.Permissions;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Contacts.UI.Blazor.Services;

public class ContactSync(UIHub hub) : ScopedWorkerBase<UIHub>(hub), IComputeService
{
    private static readonly TimeSpan ThrottlingInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 100;

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
    {
        // TODO(Frol): Please fix the bug in TrySync.
        if (OSInfo.IsWebAssembly)
            return Task.CompletedTask;

        var retryDelays = RetryDelaySeq.Exp(3, 600);
        return AsyncChain.From(TrySync)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    private async Task TrySync(CancellationToken cancellationToken)
    {
        var deviceId = DeviceContacts.DeviceId;
        if (deviceId.IsEmpty)
            return;

        while (!cancellationToken.IsCancellationRequested) {
            var cAccount = await WhenAuthenticated(cancellationToken).ConfigureAwait(false);
            var cts = cancellationToken.CreateLinkedTokenSource();
            try {
                var whenSignedOut = cAccount.When(x => x.IsGuestOrNone || x.Id != cAccount.Value.Id, cts.Token);
                var whenSynced = Sync(cAccount.Value, cts.Token);
                await Task.WhenAny(whenSynced, whenSignedOut).ConfigureAwait(false);
                if (whenSynced.IsCompletedSuccessfully)
                    return;

                // NOTE(AY): This loop never ends in WASM & prob. some other scenarios.
                // If it ends up here, more likely than not it will spin for indefinitely long time.
            }
            finally {
                cts.CancelAndDisposeSilently();
            }
        }
    }

    private async Task Sync(AccountFull account, CancellationToken cancellationToken)
    {
        await ContactsPermission.Check(cancellationToken).ConfigureAwait(false);
        await ContactsPermission.Cached.When(x => x == true, cancellationToken).ConfigureAwait(false);

        var deviceContacts = await DeviceContacts.List(cancellationToken).ConfigureAwait(false);
        var deviceRootHash = ExternalContactHasher.Compute(deviceContacts);
        var existingRootHash = await ExternalContactHashes.Get(Session, DeviceContacts.DeviceId, cancellationToken).ConfigureAwait(false);
        if (OrdinalEquals(deviceRootHash, existingRootHash?.Sha256Hash))
            return;

        var changes = await DetectChanges(deviceContacts, cancellationToken).ConfigureAwait(false);
        if (await SaveChanges(changes, cancellationToken).ConfigureAwait(false)) {
            var rootHash = existingRootHash ?? new ExternalContactsHash(new UserDeviceId(account.Id, DeviceContacts.DeviceId));
            rootHash = rootHash with { Sha256Hash = deviceRootHash };
            var changeHashCmd = new ExternalContactHashes_Change(Session,
                DeviceContacts.DeviceId,
                existingRootHash?.Version,
                Change.Upsert(rootHash));
            await Commander.Call(changeHashCmd, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<ExternalContactChange>> DetectChanges(ApiArray<ExternalContactFull> deviceContacts, CancellationToken cancellationToken)
    {
        var existingContacts = await ExternalContacts.List2(Session, DeviceContacts.DeviceId, cancellationToken).ConfigureAwait(false);
        var existingMap = existingContacts.ToDictionary(x => x.Id);
        var toAdd = deviceContacts.Where(x => !existingMap.ContainsKey(x.Id)).ToList();
        var toRemove = existingMap.Keys.Except(deviceContacts.Select(x => x.Id)).ToList();
        var toUpdate = deviceContacts.Select(deviceContact => {
                if (!existingMap.TryGetValue(deviceContact.Id, out var existing))
                    return null;

                return OrdinalEquals(existing.Sha256Hash, deviceContact.Sha256Hash)
                    ? null
                    : deviceContact with { Version = existing.Version };
            })
            .SkipNullItems()
            .ToList();

        var changes = ToRemoveChanges()
            .Concat(ToChanges(toUpdate, Change.Update))
            .Concat(ToChanges(toAdd, Change.Create))
            .ToList();
        return changes;

        IEnumerable<ExternalContactChange> ToRemoveChanges()
            => toRemove.Select(x => new ExternalContactChange(x, null, Change.Remove<ExternalContactFull>()));

        IEnumerable<ExternalContactChange> ToChanges(
            IEnumerable<ExternalContactFull> externalContacts,
            Func<ExternalContactFull, Change<ExternalContactFull>> toChange)
            => externalContacts.Select(x => new ExternalContactChange(x.Id, x.Version, toChange(x)));
    }

    private async Task<bool> SaveChanges(IEnumerable<ExternalContactChange> changes, CancellationToken cancellationToken)
    {
        var success = true;
        foreach (var bulk in changes.Chunk(BatchSize)) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
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
            catch (Exception e) {
                Log.LogWarning(e, "Failed to sync {Count} contacts", bulk.Length);
                success = false;
            }
        }

        return success;
    }

    private Task<Computed<AccountFull>> WhenAuthenticated(CancellationToken cancellationToken)
        => AccountUI.OwnAccount.When(x => !x.IsGuestOrNone, cancellationToken);
}
