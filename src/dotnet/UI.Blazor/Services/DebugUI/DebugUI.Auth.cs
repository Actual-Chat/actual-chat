
namespace ActualChat.UI.Blazor.Services;

// Sign-in / sign-out / get-own-user-id — extracted from the main DebugUI.cs
// because SignIn alone is the longest method on this type.
public sealed partial class DebugUI
{
    [JSInvokable]
    public Task<string> GetUserId()
        => Task.FromResult(Hub.AccountUI.OwnAccount.Value.Id.Value);

    /// <summary>
    /// Local-dev-only: scripts the sign-in flow that the modal would normally
    /// drive — request a TOTP, validate it with the dev-bypass code, confirm
    /// the pending registration if needed, wait for the client-side
    /// AccountUI to observe the new account, and (optionally) clear
    /// onboarding/bubbles. Mirrors the same validation gates as
    /// <see cref="StopServer"/>.
    /// </summary>
    /// <param name="phoneOrEmail">An email like <c>test-foo@actual.chat</c>
    /// or a phone in any sensible format
    /// (<c>+1 555 555 5550</c> / <c>15555555550</c> / <c>1-5555555550</c>).</param>
    /// <param name="register">Confirm the pending registration if the account
    /// doesn't yet exist.</param>
    /// <param name="skipOnboarding">Mark all onboarding steps complete.</param>
    /// <param name="skipBubbles">Mark all feature bubbles complete.</param>
    /// <remarks>
    /// Uses the dev-bypass TOTP <c>111111</c> (matches the predefined values
    /// the loop wires up for <c>+1 555 555 5550..5555</c> and the
    /// <c>test-*@actual.chat</c> dev shortcut). Other inputs will fail TOTP
    /// validation.
    /// </remarks>
    [JSInvokable]
    public async Task SignIn(string phoneOrEmail, bool register = true, bool skipOnboarding = true, bool skipBubbles = true)
    {
        if (HostInfo is not { IsDevelopmentInstance: true, BaseUrlKind: BaseUrlKind.Local })
            throw StandardError.Unauthorized("SignIn works on local-dev server instances only.");

        var session = Hub.Session;
        var commander = Hub.Commander;
        var input = (phoneOrEmail ?? "").Trim();
        if (input.Length == 0)
            throw StandardError.Constraint("phoneOrEmail must be non-empty.");

        if (input.Contains('@')) {
            var email = Email.Parse(input);
            await commander.Call(new EmailAuth_SendTotp(session, email)).ConfigureAwait(true);
            var ok = await commander.Call(new EmailAuth_ValidateTotp(session, email, 111111)).ConfigureAwait(true);
            if (!ok)
                throw StandardError.Internal($"EmailAuth.ValidateTotp failed for '{email}'.");
        }
        else {
            var phone = await Hub.Phones.ParseWithCountryFallback(session, input, default).ConfigureAwait(true)
                ?? throw StandardError.Constraint($"Cannot parse phone '{input}'.");
            // Keeps the legacy SendTotp path exercised from the debug UI
#pragma warning disable CS0618
            await commander.Call(new PhoneAuth_SendTotp(session, phone)).ConfigureAwait(true);
#pragma warning restore CS0618
            var ok = await commander.Call(new PhoneAuth_ValidateTotp(session, phone, 111111)).ConfigureAwait(true);
            if (!ok)
                throw StandardError.Internal(
                    $"PhoneAuth.ValidateTotp failed for '{phone}'.");
        }

        if (register) {
            var json = await Hub.SessionTemporals
                .Get(session, Constants.SessionTemporals.PendingRegistrationKey, default)
                .ConfigureAwait(true);
            if (PendingRegistrationInfo.TryParseJson(json) is { } info)
                await commander.Call(new Accounts_ConfirmRegister(session, info.Token)).ConfigureAwait(true);
        }

        // Wait for the client-side AccountUI to observe the new (non-guest)
        // account before mutating onboarding/bubble state — those rely on
        // OwnAccount being settled. 5s is generous; locally it's typically <1s.
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) {
            try {
                await Hub.AccountUI.OwnAccount.Computed
                    .When(x => !x.IsGuest, cts.Token)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) {
                throw StandardError.Internal("SignIn timed out waiting for AccountUI.OwnAccount to become non-guest after 5s.");
            }
        }

        // OwnAccount flipping to non-guest fires before the new circuit's
        // BubbleHost has registered its JS reference. Calling
        // BubbleUI.ResetBubbles in that window throws ArgumentNullException
        // ("jsObjectReference is null"). 2s settles things in practice.
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);

        if (skipOnboarding)
            Hub.OnboardingUI.ResetOnboarding(false);
        if (skipBubbles)
            await Hub.BubbleUI.ResetBubbles(false).ConfigureAwait(true);

        Log.LogInformation(
            "SignIn('{Input}', register={Register}, skipOnboarding={SkipOnboarding}, skipBubbles={SkipBubbles}): done",
            input, register, skipOnboarding, skipBubbles);
    }

    [JSInvokable]
    public async Task SignOut()
    {
        await Hub.AccountUI.SignOut().ConfigureAwait(true);
        Log.LogInformation("SignOut: done");
    }
}
