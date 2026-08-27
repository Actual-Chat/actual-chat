using ActualChat.Localization;
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
            AsyncChain.From(MonitorSessionValidity),
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

    private async Task MonitorSessionValidity(CancellationToken cancellationToken)
    {
        // Nothing validates the session up front - it is used optimistically, and this is what
        // notices the server rejecting it. Sign-out deactivates it, and it can also be killed from
        // another device or expire; whichever it is, the session has to be replaced to stay usable.
        var cSessionInfo0 = await Computed
            .Capture(() => Accounts.GetSessionInfo(Session, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cSessionInfo0.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
        await foreach (var cSessionInfo in changes.ConfigureAwait(false)) {
            var (sessionInfo, error) = cSessionInfo;
            // An error means we couldn't ask, not that the answer is no - offline keeps the session
            if (error != null || sessionInfo is { IsActive: true })
                continue;
            // Fusion serves the last cached value while the peer reconnects - and every session
            // switch disconnects it - so an unsynchronized answer describes the session we replaced.
            // Precise, because Safe assumes synchronized exactly when disconnected.
            if (!cSessionInfo.IsSynchronized(ComputedSynchronizer.Precise.Instance))
                continue;

            Log.LogWarning("Session is no longer valid - replacing it");
            // Recreating the WebView while the first scope is still initializing leaves a
            // half-disposed scope with a disconnected JS runtime, so let it finish rendering first.
            await Hub.WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
            var isReplaced = await ReloadUI
                .ReplaceSession(sessionInfo?.Session, cancellationToken)
                .ConfigureAwait(false);
            if (!isReplaced) {
                // The session we saw as dead isn't the current one anymore, so this was a cached
                // answer from before someone else replaced it - wait for one that isn't.
                Log.LogWarning("Session was already replaced - that answer was a stale one");
                continue;
            }

            return; // The reload owns everything past this point
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
                ReloadUI.Reload(clearLocalSettings: true); // And were signed in -> it's a sign-out
            return;
        }

        // We're signed in now
        if (!oldAccount.IsGuestOrNull()) {
            // And were signed in -> it's an account change
            ReloadUI.Reload(clearLocalSettings: true);
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

        if (!History.LocalUrl.IsChatOrChatRoot() && !History.LocalUrl.IsSettings())
            _ = AutoNavigationUI.NavigateTo(Links.Chats, AutoNavigationReason.SignIn);
    }

    private async Task ShowPendingRegistrationModal(PendingRegistrationInfo info)
    {
        var session = Session;
        var commander = Services.Commander();
        var isConfirmed = false;

        var identifier = info.Identifier.NullIfEmpty() ?? L.Account_RegisterThisAccount;
        var text = L.Account_RegisterText_Format(identifier);
        var model = new ConfirmModal.Model(
            IsDestructive: false,
            Text: text,
            Confirm: () => {
                isConfirmed = true;
                _ = commander.Run(new Accounts_ConfirmRegister {
                    Session = session,
                    Token = info.Token,
                }, true, CancellationToken.None);
            }) {
            Title = L.Account_RegisterTitle,
            ConfirmButtonText = L.Account_Register,
        };
        var modalRef = await Hub.ModalUI.Show(model).ConfigureAwait(true);
        await modalRef.WhenClosed.ConfigureAwait(true);

        if (!isConfirmed && _pendingRegistrationToken == info.Token) {
            // User dismissed without confirming — clear the prompt and show a sign-in error.
            _ = commander.Run(new Accounts_CancelRegister {
                Session = session,
                Token = info.Token,
            }, true, CancellationToken.None);
        }
    }
}
