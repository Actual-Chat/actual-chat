using ActualChat.Users;
using Stl.Fusion.Client.Caching;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    // All state sync logic should be here

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return new AsyncChain(nameof(MonitorAccountChange), MonitorAccountChange)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    private async Task MonitorAccountChange(CancellationToken cancellationToken)
    {
        var oldAccount = OwnAccount.Value;
        var cOwnAccount = await Computed
            .Capture(() => Accounts.GetOwn(Session, cancellationToken))
            .ConfigureAwait(false);
        if (cOwnAccount.ValueOrDefault is { } ownAccount)
            SetOwnAccount(ownAccount, false);
        cOwnAccount = await cOwnAccount.UpdateIfCached(cancellationToken);
        var changes = cOwnAccount.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var (newAccount, error) in changes.ConfigureAwait(false)) {
            if (error != null || newAccount == null!)
                continue;

            if (ReferenceEquals(oldAccount, newAccount)) {
                // The cached value is still intact
                _whenLoadedFromServerSource.TrySetResult();
                continue;
            }

            Log.LogInformation("Update: new account: {Account}", newAccount);
            SetOwnAccount(newAccount, true);
            if (oldAccount.Id == newAccount.Id) {
                oldAccount = newAccount;
                continue; // Only account properties have changed
            }

            var oldAccountCopy = oldAccount; // Just to avoid "captured var is modified in closure" warning
            await BlazorCircuitContext.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            await BlazorCircuitContext.Dispatcher
                .InvokeAsync(() => ProcessOwnAccountChange(newAccount, oldAccountCopy))
                .ConfigureAwait(false);
            oldAccount = newAccount;
        }
    }

    // Private methods

    private void SetOwnAccount(AccountFull account, bool isLoadedFromServer)
    {
        // NOTE(AY): Set(_ => ...) below ensures equality comparison doesn't happen,
        // and we want to avoid it here, coz Account changes when its Avatar changes,
        // but this change happens w/o its Version change (avatars are stored in
        // account settings - see Avatars_SetDefault command handler), thus
        // Account.EqualityComparer won't see such changes.
        _ownAccount.Set(_ => account);
        _whenLoadedSource.TrySetResult();
        if (isLoadedFromServer)
            _whenLoadedFromServerSource.TrySetResult();
    }

    private async Task ProcessOwnAccountChange(AccountFull account, AccountFull oldAccount)
    {
        OwnAccountChanged?.Invoke(account);
        var history = Services.GetRequiredService<History>();
        var autoNavigationUI = Services.GetRequiredService<AutoNavigationUI>();
        if (account.IsGuestOrNone || !oldAccount.IsGuestOrNone) {
            // Sign-out or account change
            var clientComputedCache = Services.GetService<IClientComputedCache>();
            if (clientComputedCache != null)
                await clientComputedCache.Clear(CancellationToken.None).ConfigureAwait(true);
        }

        if (account.IsGuestOrNone) {
            // Sign-out
            await autoNavigationUI.NavigateTo(Links.Home, AutoNavigationReason.SignOut);
        }
        else {
            // Sign-in or account change
            if (history.LocalUrl.IsChatOrChatRoot())
                return;

            await autoNavigationUI.NavigateTo(Links.Chats, AutoNavigationReason.SignIn);
        }
    }
}
