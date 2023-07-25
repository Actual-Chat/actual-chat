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
        Log.LogInformation(nameof(MonitorAccountChange));
        var cOwnAccount0 = await Computed
            .Capture(() => Accounts.GetOwn(Session, cancellationToken))
            .ConfigureAwait(false);
        var changes = cOwnAccount0.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cOwnAccount in changes.ConfigureAwait(false)) {
            var (newAccount, error) = cOwnAccount;
            if (error != null || newAccount == null!)
                continue;

            if (!TrySetOwnAccount(newAccount, out var oldAccount))
                continue;

            Log.LogInformation("Account changed to: {Account}", newAccount);
            if (oldAccount.Id == newAccount.Id)
                continue; // Only account properties have changed

            _lastChangedAt.Value = Clock.Now;
            await BlazorCircuitContext.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            await BlazorCircuitContext.Dispatcher
                .InvokeAsync(() => ProcessOwnAccountChange(newAccount, oldAccount))
                .ConfigureAwait(false);
        }
    }

    // Private methods

    private bool TrySetOwnAccount(AccountFull account, out AccountFull oldAccount)
    {
        // NOTE(AY): Set(_ => ...) below ensures equality comparison doesn't happen,
        // and we want to avoid it here, coz Account changes when its Avatar changes,
        // but this change happens w/o its Version change (avatars are stored in
        // account settings - see Avatars_SetDefault command handler), thus
        // Account.EqualityComparer won't see such changes.

        oldAccount = _ownAccount.Value;
        var isChanged = !ReferenceEquals(oldAccount, account);
        if (isChanged)
            _ownAccount.Set(_ => account);
        _whenLoadedSource.TrySetResult();
        return isChanged;
    }

    private async Task ProcessOwnAccountChange(AccountFull account, AccountFull oldAccount)
    {
        Changed?.Invoke(account);
        if (account.IsGuestOrNone || !oldAccount.IsGuestOrNone) {
            // Sign-out or account change
            var localSettings = Services.GetRequiredService<LocalSettings>();
            var clearLocalSettingsTask = localSettings.Clear();
            var clientComputedCache = Services.GetService<IClientComputedCache>();
            if (clientComputedCache != null)
                await clientComputedCache.Clear(CancellationToken.None);
            await clearLocalSettingsTask;
        }

        var history = Services.GetRequiredService<History>();
        var autoNavigationUI = Services.GetRequiredService<AutoNavigationUI>();
        if (account.IsGuestOrNone) {
            // Sign-out
            _ = autoNavigationUI.NavigateTo(Links.Home, AutoNavigationReason.SignOut);
        }
        else {
            // Sign-in or account change
            var onboardingUI = Services.GetRequiredService<IOnboardingUI>();
            _ = onboardingUI.TryShow();
            if (history.LocalUrl.IsChatOrChatRoot())
                return;

            _ = autoNavigationUI.NavigateTo(Links.Chats, AutoNavigationReason.SignIn);
        }
    }
}
