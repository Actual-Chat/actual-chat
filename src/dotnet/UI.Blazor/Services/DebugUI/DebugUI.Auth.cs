
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
    /// <remarks>
    /// The TOTP is always <c>111111</c>, which is built in for
    /// <c>test-*@actual.chat</c> and <c>+1 555 555 5550..5559</c>; anything else fails validation.
    /// </remarks>
    [JSInvokable]
    public async Task SignIn(string phoneOrEmail, bool register = true, bool skipOnboarding = true, bool skipBubbles = true)
    {
        if (!HostInfo.IsLocalDevInstance())
            throw StandardError.Unauthorized("SignIn works on local-dev server instances only.");

        var input = (phoneOrEmail ?? "").Trim();
        if (input.Length == 0)
            throw StandardError.Constraint("phoneOrEmail must be non-empty.");

        // Suppressed from before the auth calls, since sign-in is what starts onboarding and tips,
        // until the skips reach the account's settings, which is possible only after it
        var suppressedKinds = (skipOnboarding ? AttentionKind.Onboarding : AttentionKind.None)
            | (skipBubbles ? AttentionKind.Bubbles : AttentionKind.None);
        using var suppression = Hub.AttentionUI.Suppress(suppressedKinds);
        // A new account signs in once its registration is confirmed; without an action AccountUI would ask the user
        if (register)
            Hub.AccountUI.PendingRegistrationAction = ConfirmRegistration;
        try {
            await Authenticate(input).ConfigureAwait(true);
            // Wait for the client-side AccountUI to observe the new (non-guest)
            // account, so callers see it signed in. 5s is generous; locally it's typically <1s.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try {
                await Hub.AccountUI.OwnAccount.Computed
                    .When(x => !x.IsGuest, cts.Token)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) {
                throw StandardError.Internal(
                    "SignIn timed out waiting for AccountUI.OwnAccount to become non-guest after 5s.");
            }
        }
        finally {
            if (register)
                Hub.AccountUI.PendingRegistrationAction = null;
        }

        if (skipOnboarding)
            await Hub.OnboardingUI.ResetOnboarding(false).ConfigureAwait(true);
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

    // Private methods

    private async Task Authenticate(string input)
    {
        var session = Hub.Session;
        var commander = Hub.Commander;
        if (input.Contains('@')) {
            var email = Email.Parse(input);
            await commander.Call(new EmailAuth_SendTotp { Session = session, Email = email }).ConfigureAwait(true);
            var ok = await commander.Call(new EmailAuth_ValidateTotp {
                Session = session,
                Email = email,
                Totp = 111111,
            }).ConfigureAwait(true);
            if (!ok)
                throw StandardError.Internal($"EmailAuth.ValidateTotp failed for '{email}'.");
        }
        else {
            var phone = await Hub.Phones.ParseWithCountryFallback(session, input, default).ConfigureAwait(true)
                ?? throw StandardError.Constraint($"Cannot parse phone '{input}'.");
            // Keeps the legacy SendTotp path exercised from the debug UI
#pragma warning disable CS0618
            await commander.Call(new PhoneAuth_SendTotp { Session = session, Phone = phone }).ConfigureAwait(true);
#pragma warning restore CS0618
            var ok = await commander.Call(new PhoneAuth_ValidateTotp {
                Session = session,
                Phone = phone,
                Totp = 111111,
            }).ConfigureAwait(true);
            if (!ok)
                throw StandardError.Internal(
                    $"PhoneAuth.ValidateTotp failed for '{phone}'.");
        }
    }

    private Task ConfirmRegistration(PendingRegistrationInfo info)
        => Hub.Commander.Call(new Accounts_ConfirmRegister { Session = Hub.Session, Token = info.Token });
}
