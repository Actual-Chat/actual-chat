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
        oldAccount = _ownAccount.Value;
        var isChanged = !ReferenceEquals(oldAccount, account);
        if (isChanged)
            _ownAccount.Value = account;
        _whenLoadedSource.TrySetResult();
        return isChanged;
    }

    private void ProcessOwnAccountChange(AccountFull account, AccountFull oldAccount)
    {
        Changed?.Invoke(account);
        var reloadUI = Services.GetRequiredService<ReloadUI>();
        var history = Services.GetRequiredService<History>();
        var autoNavigationUI = Services.GetRequiredService<AutoNavigationUI>();
        if (account.IsGuestOrNone) {
            // We're signed out now
            if (!oldAccount.IsGuestOrNone)
                reloadUI.Reload(true); // And were signed in -> it's a sign-out
            return;
        }

        // We're signed in now
        if (!oldAccount.IsGuestOrNone) {
            // And were signed in -> it's an account change
            reloadUI.Reload(true);
            return;
        }

        // We were signed out -> it's a sign-in
        var onboardingUI = Services.GetRequiredService<IOnboardingUI>();
        _ = onboardingUI.TryShow();
        if (!history.LocalUrl.IsChatOrChatRoot())
            _ = autoNavigationUI.NavigateTo(Links.Chats, AutoNavigationReason.SignIn);
    }
}
