using ActualChat.UI.Blazor.Events;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    // All state sync logic should be here

    protected override Task OnRun(CancellationToken cancellationToken)
        => Task.WhenAll(
            SyncOwnAccount(cancellationToken),
            Task.CompletedTask); // Just to add more items w/o need to worry about comma :)

    private async Task SyncOwnAccount(CancellationToken cancellationToken)
    {
        var uiEventHub = (UIEventHub?)null;
        var cOwnAccount0 = await Computed
            .Capture(() => Accounts.GetOwn(Session, cancellationToken))
            .ConfigureAwait(false);
        cOwnAccount0 = await cOwnAccount0.UpdateIfCached(TimeSpan.FromSeconds(2), cancellationToken);
        var changes = cOwnAccount0.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cOwnAccount in changes.ConfigureAwait(false)) {
            if (cOwnAccount.HasError)
                continue;

            var ownAccount = cOwnAccount.Value;
            if (ownAccount is not { Id.IsNone: false })
                continue;

            var oldAccount = _ownAccount.Value;
            if (oldAccount == ownAccount)
                continue;

            Log.LogDebug("SyncOwnAccount: new OwnAccount: {Account}", ownAccount);
            _ownAccount.Value = ownAccount;
            if (!_whenLoadedSource.TrySetResult()) {
                // We don't publish this event for the initial account change
                uiEventHub ??= Services.GetRequiredService<UIEventHub>();
                var @event = new OwnAccountChangedEvent(ownAccount, oldAccount);
                await uiEventHub.Publish(@event, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
