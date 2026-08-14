using ActualChat.UI.Blazor.Resources;
namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    // All state sync logic should be here

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        var chains = new[] {
            AsyncChain.From(MonitorAccountChange),
            AsyncChain.From(MonitorPendingRegistration),
        };
        return Task.WhenAll(chains.Select(c => c
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken)));
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

            if (!TryChangeAccount(newAccount, out var oldAccount))
                continue;
            if (oldAccount is null) {
                MarkReady();
                continue; // Very first account change
            }
            if (oldAccount.Id == newAccount.Id)
                continue; // Only account properties have changed

            Log.LogInformation("Account is changed to: {Account}", newAccount);
            _lastChangedAt.Value = CpuClock.Now;
            await SaveSignedInState(newAccount).ConfigureAwait(false);
            await Hub.WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
            await Hub.Dispatcher
                .InvokeSafeAsync(() => ProcessLoginLogout(newAccount, oldAccount), Log)
                .ConfigureAwait(false);
        }
    }

    private async Task MonitorPendingRegistration(CancellationToken cancellationToken)
    {
        var key = Constants.SessionTemporals.PendingRegistrationKey;
        var cTemporal0 = await Computed
            .Capture(() => Hub.SessionTemporals.Get(Session, key, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cTemporal0.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
        await foreach (var cTemporal in changes.ConfigureAwait(false)) {
            var (json, error) = cTemporal;
            if (error != null)
                continue;

            var info = PendingRegistrationInfo.TryParseJson(json);
            if (info is null) {
                _pendingRegistrationToken = null;
                continue;
            }
            if (info.Token == _pendingRegistrationToken)
                continue; // Same prompt is already shown — don't reopen

            _pendingRegistrationToken = info.Token;
            await Hub.WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
            var infoCopy = info;
            try {
                await Hub.Dispatcher
                    .InvokeAsync(() => ShowPendingRegistrationModal(infoCopy))
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to show pending-registration modal");
            }
        }
    }

    // Private methods

    private void MarkReady()
    {
        if (_whenReadySource.TrySetResult())
            // ReSharper disable once ExplicitCallerInfoArgument
            Tracer.Point("AccountUI is ready");
    }

    private bool TryChangeAccount(AccountFull account, out AccountFull? oldAccount)
    {
        oldAccount = _ownAccount.Value;
        if (oldAccount == account)
            return false;

        _ownAccount.Value = account;
        return true;
    }

    private void ProcessLoginLogout(AccountFull? account, AccountFull? oldAccount)
    {
        LoginLogout?.Invoke(account);
        if (account.IsGuestOrNull()) {
            // We're signed out now
            if (!oldAccount.IsGuestOrNull())
                ReloadUI.Reload(true, true); // And were signed in -> it's a sign-out
            return;
        }

        // We're signed in now
        if (!oldAccount.IsGuestOrNull()) {
            // And were signed in -> it's an account change
            ReloadUI.Reload(true, true);
            return;
        }

        _ = StartOnSignedInWorkflow();
    }

    private async Task SaveSignedInState(AccountFull? account)
    {
        var isSignedIn = !account.IsGuestOrNull();
        await LocalStorage.SetString("AccountUI.IsSignedIn", isSignedIn ? "1" : "0").ConfigureAwait(false);
    }

    private async Task StartOnSignedInWorkflow()
    {
        DebugLog?.LogInformation("Starting OnSignedInWorkflow");
        await PostponeOnSignedInWorkflow().ConfigureAwait(true);

        // We were signed out -> it's a sign-in
        _ = OnboardingUI.TryShow();
        if (_activeSignInRequest.Value != null)
            return; // No auto-navigation in this case

        if (!History.LocalUrl.IsChatOrChatRoot() && !History.LocalUrl.IsSettings() )
            _ = AutoNavigationUI.NavigateTo(Links.Chats, AutoNavigationReason.SignIn);
    }

    private async Task ShowPendingRegistrationModal(PendingRegistrationInfo info)
    {
        var session = Session;
        var commander = Services.Commander();
        var confirmed = false;

        var identifier = info.Identifier.NullIfEmpty() ?? L.Account_RegisterThisAccount;
        var text = L.Account_RegisterText_Format(identifier);
        var model = new ConfirmModal.Model(
            IsDestructive: false,
            Text: text,
            Confirm: () => {
                confirmed = true;
                _ = commander.Run(new Accounts_ConfirmRegister(session, info.Token), true, CancellationToken.None);
            }) {
            Title = L.Account_RegisterTitle,
            ConfirmButtonText = L.Account_Register,
        };
        var modalRef = await Hub.ModalUI.Show(model).ConfigureAwait(true);
        await modalRef.WhenClosed.ConfigureAwait(true);

        if (!confirmed && _pendingRegistrationToken == info.Token) {
            // User dismissed without confirming — clear the prompt and show a sign-in error.
            _ = commander.Run(new Accounts_CancelRegister(session, info.Token), true, CancellationToken.None);
        }
    }
}
