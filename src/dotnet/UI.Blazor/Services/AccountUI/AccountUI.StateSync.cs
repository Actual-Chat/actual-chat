using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    // All state sync logic should be here

    protected override Task RunInternal(CancellationToken cancellationToken)
        => Task.WhenAll(
            SyncOwnAccount(cancellationToken),
            Task.CompletedTask); // Just to add more items w/o need to worry about comma :)

    private async Task SyncOwnAccount(CancellationToken cancellationToken)
    {
        TraceSession.Track("[AccountUI] Start SyncOwnAccount");
        var cOwnAccount0 = await Computed
            .Capture(() => Accounts.GetOwn(Session, cancellationToken))
            .ConfigureAwait(false);
        var changes = cOwnAccount0.Changes(cancellationToken);
        await foreach (var cOwnAccount in changes.ConfigureAwait(false)) {
            if (cOwnAccount.HasError)
                continue;

            var ownAccount = cOwnAccount.Value;
            if (ownAccount is not { Id.IsNone: false })
                continue;

            var hasLoaded = _ownAccount.Value == AccountFull.Loading;

            _ownAccount.Value = ownAccount;
            _whenLoadedSource.TrySetResult(default);
            if (hasLoaded)
                TraceSession.Track("[AccountUI] OwnAccount has loaded");
        }
    }
}
