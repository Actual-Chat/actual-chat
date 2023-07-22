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
        var cOwnAccount = await Computed
            .Capture(() => Accounts.GetOwn(Session, cancellationToken))
            .ConfigureAwait(false);
        cOwnAccount = await cOwnAccount.UpdateIfCached(TimeSpan.FromSeconds(2), cancellationToken);
        var changes = cOwnAccount.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var (newAccount, error) in changes.ConfigureAwait(false)) {
            if (error != null || newAccount == null!)
                continue;

            var oldAccount = OwnAccount.Value;
            if (ReferenceEquals(oldAccount, newAccount))
                continue;

            Log.LogInformation("Update: new account: {Account}", newAccount);
            // NOTE(AY): Set(_ => ...) below ensures equality comparison doesn't happen,
            // and we want to avoid it here, coz Account changes when its Avatar changes,
            // but this change happens w/o its Version change (avatars are stored in
            // account settings - see Avatars_SetDefault command handler), thus
            // Account.EqualityComparer won't see such changes.
            _ownAccount.Set(_ => newAccount);
            if (_whenLoadedSource.TrySetResult())
                continue; // It's an initial account change

            if (oldAccount.Id == newAccount.Id)
                continue; // Only account properties have changed

            await BlazorCircuitContext.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            await BlazorCircuitContext.Dispatcher
                .InvokeAsync(() => ProcessOwnAccountChange(newAccount, oldAccount))
                .ConfigureAwait(false);
        }
    }

    // Private methods

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
