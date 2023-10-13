using ActualChat.Permissions;
using ActualChat.Users;

namespace ActualChat.Contacts.UI.Blazor.Services;

public class ContactSync(IServiceProvider services) : WorkerBase, IComputeService
{
    private Session Session { get; } = services.Session();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IExternalContacts ExternalContacts { get; } = services.GetRequiredService<IExternalContacts>();
    private DeviceContacts DeviceContacts { get; } = services.GetRequiredService<DeviceContacts>();
    private ContactsPermissionHandler ContactsPermission { get; } = services.GetRequiredService<ContactsPermissionHandler>();
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();
    private ICommander Commander { get; } = services.Commander();
    private ILogger Log { get; } = services.LogFor<ContactSync>();

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChainExt.From(EnsureGreeted),
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

    private async Task EnsureGreeted(CancellationToken cancellationToken)
    {
        // TODO: this code is backing up ExternalContactsBackend.OnUserEvent
        var (account, _) = await WhenAuthenticated(cancellationToken).ConfigureAwait(false);
        if (account.IsGreetingCompleted)
            return;

        await Commander.Call(new Contacts_Greet(Session), cancellationToken).ConfigureAwait(false);
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

        var commands = ToCommands(toRemove, Change.Remove)
            .Concat(ToCommands(toUpdate, Change.Update))
            .Concat(ToCommands(toAdd, Change.Create))
            .ToList();

        var errors = new List<Exception>();
        foreach (var command in commands) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                Log.LogWarning(e, "Failed to sync contact {ContactId}", command.Id.DeviceContactId);
                errors.Add(e);
                await Task.Delay(TimeSpan.FromSeconds(5000), cancellationToken).ConfigureAwait(false);
            }
        }
        if (errors.Count > 0)
            throw new AggregateException("Some errors occured while syncing contacts", errors);

        return;

        IEnumerable<ExternalContacts_Change> ToCommands(
            IEnumerable<ExternalContact> externalContacts,
            Func<ExternalContact, Change<ExternalContact>> toChange)
            => externalContacts.Select(x => new ExternalContacts_Change(Session, x.Id, x.Version, toChange(x)));
    }

    private Task<Computed<AccountFull>> WhenAuthenticated(CancellationToken cancellationToken)
        => Computed.Capture(() => Accounts.GetOwn(Session, cancellationToken))
            .When(x => x.IsActive(), cancellationToken);
}
