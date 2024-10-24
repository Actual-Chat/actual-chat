using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    // All state sync logic should be here

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return AsyncChain.From(MonitorAccountChange)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    private async Task MonitorAccountChange(CancellationToken cancellationToken)
    {
        Log.LogInformation(nameof(MonitorAccountChange));
        var cOwnAccount0 = await Computed
            .Capture(() => Accounts.GetOwn(Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cOwnAccount0.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
        await foreach (var cOwnAccount in changes.ConfigureAwait(false)) {
            var (newAccount, error) = cOwnAccount;
            if (error != null || newAccount == null!)
                continue;

            if (!TrySetOwnAccount(newAccount, out var oldAccount))
                continue;

            Log.LogInformation("Account changed to: {Account}", newAccount);
            if (oldAccount.Id == newAccount.Id)
                continue; // Only account properties have changed

            _lastChangedAt.Value = CpuClock.Now;
            var circuitContext = CircuitContext;
            await circuitContext.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            await circuitContext.Dispatcher
                .InvokeSafeAsync(() => ProcessOwnAccountChange(newAccount, oldAccount), Log)
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
        if (account.IsGuestOrNone) {
            // We're signed out now
            if (!oldAccount.IsGuestOrNone)
                ReloadUI.Reload(true, true); // And were signed in -> it's a sign-out
            return;
        }

        // We're signed in now
        if (!oldAccount.IsGuestOrNone) {
            // And were signed in -> it's an account change
            ReloadUI.Reload(true, true);
            return;
        }

        _ = StartOnSignedInWorkflow();
    }

    private async Task StartOnSignedInWorkflow()
    {
        DebugLog?.LogInformation("Starting OnSignedInWorkflow");
        await PostponeOnSignedInWorkflow().ConfigureAwait(true); // Continue on Blazor UI Context.

        // We were signed out -> it's a sign-in
        _ = OnboardingUI.TryShow();
        if (_activeSignInRequest.Value != null)
            return; // No auto-navigation in this case

        if (!History.LocalUrl.IsChatOrChatRoot() && !History.LocalUrl.IsSettings() )
            _ = AutoNavigationUI.NavigateTo(Links.Chats, AutoNavigationReason.SignIn);
    }
}
