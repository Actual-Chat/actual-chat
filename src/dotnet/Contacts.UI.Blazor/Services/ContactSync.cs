using ActualChat.Users;

namespace ActualChat.Contacts.UI.Blazor.Services;

public class ContactSync(IServiceProvider services) : WorkerBase, IComputeService
{
    private Session Session { get; } = services.Session();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IExternalContacts ExternalContacts { get; } = services.GetRequiredService<IExternalContacts>();
    private DeviceContacts DeviceContacts { get; } = services.GetRequiredService<DeviceContacts>();
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();
    private UICommander UICommander { get; } = services.UICommander();
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

        await UICommander.Call(new Contacts_Greet(Session), cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncUntilSignedOut(CancellationToken cancellationToken)
    {
        var deviceId = DeviceContacts.DeviceId;
        if (deviceId.IsEmpty)
            return;

        using var cts = cancellationToken.CreateLinkedTokenSource();
        var cAccount = await WhenAuthenticated(cancellationToken).ConfigureAwait(false);
        await Task.WhenAny(WhenSignedOut(cts.Token), Sync(cts.Token)).ConfigureAwait(false);
        cts.Cancel();
        return;

        Task WhenSignedOut(CancellationToken cancellationToken1)
            => cAccount.When(x => x.IsGuestOrNone || x.Id != cAccount.Value.Id, cancellationToken1);
    }

    private async Task Sync(CancellationToken cancellationToken)
    {
        var existingContacts = await ExternalContacts.List(Session, DeviceContacts.DeviceId, cancellationToken).ConfigureAwait(false);
        var existingMap = existingContacts.ToDictionary(x => x.Id);
        var deviceContacts = await DeviceContacts.List(cancellationToken).ConfigureAwait(false);

        var toAdd = deviceContacts.Where(x => !existingMap.ContainsKey(x.Id)).ToList();
        var toRemove = existingContacts.ExceptBy(deviceContacts.Select(x => x.Id), x => x.Id).ToList();
        var toUpdate = deviceContacts
            .Select(x =>
            {
                if (!existingMap.TryGetValue(x.Id, out var externalContact))
                    return null;

                var diff = DiffEngine.Diff<ExternalContact, ExternalContactDiff>(x, externalContact);
                if (diff == ExternalContactDiff.Empty)
                    return null;

                externalContact = DiffEngine.Patch(externalContact, diff);
                return externalContact;
            })
            .SkipNullItems()
            .ToList();

        var commands = ToCommands(toRemove, Change.Remove)
            .Concat(ToCommands(toUpdate, Change.Update))
            .Concat(ToCommands(toAdd, Change.Create))
            .ToList();

        foreach (var cmd in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (_, error) = await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
            var isCancelled = error is OperationCanceledException && cancellationToken.IsCancellationRequested;
            if (error != null && !isCancelled)
                Log.LogError(error, "Failed to sync external contact {Id}", cmd.Id);
        }
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
