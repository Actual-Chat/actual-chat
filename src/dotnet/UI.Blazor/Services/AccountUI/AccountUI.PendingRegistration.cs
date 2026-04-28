using ActualChat.UI.Blazor.Components;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    private string? _pendingRegistrationToken;

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

    private async Task ShowPendingRegistrationModal(PendingRegistrationInfo info)
    {
        var session = Session;
        var commander = Services.Commander();
        var confirmed = false;

        var identifier = info.Identifier.NullIfEmpty() ?? "this account";
        var text = $"We didn't find an account for {identifier}. Would you like to register a new one?";
        var model = new ConfirmModal.Model(
            IsDestructive: false,
            Text: text,
            Confirm: () => {
                confirmed = true;
                _ = commander.Run(new Accounts_ConfirmRegister(session, info.Token), true, CancellationToken.None);
            }) {
            Title = "Create new account?",
            ConfirmButtonText = "Register",
        };
        var modalRef = await Hub.ModalUI.Show(model).ConfigureAwait(true);
        await modalRef.WhenClosed.ConfigureAwait(true);

        if (!confirmed && _pendingRegistrationToken == info.Token) {
            // User dismissed without confirming — clear the prompt and show a sign-in error.
            _ = commander.Run(new Accounts_CancelRegister(session, info.Token), true, CancellationToken.None);
        }
    }
}
