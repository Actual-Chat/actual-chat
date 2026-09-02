using ActualChat.Users.Module;

namespace ActualChat.Users;

/// <summary>
/// Verifies the action-bound captcha proof carried by TOTP send commands.
/// A missing proof is rejected only where <see cref="IsProofRequired"/> holds
/// and the session isn't a native app one - such apps have no captcha.
/// </summary>
public sealed class CaptchaProofValidator(IServiceProvider services)
{
    private const string MissingProofMessage = "Please retry from the app, or update it to the latest version.";
    private const string InvalidProofMessage = "We couldn't confirm this request. Please try again.";

    private HostInfo HostInfo { get; } = services.HostInfo();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private ICaptcha Captcha => field ??= services.GetRequiredService<ICaptcha>();
    private ISessionsBackend SessionsBackend => field ??= services.GetRequiredService<ISessionsBackend>();
    private ILogger Log => field ??= services.LogFor(GetType());

    public bool IsProofRequired
        // The same flag that decides whether the real captcha is served at all: a host that doesn't
        // serve it runs the fake one, whose token is minted client-side and so proves nothing.
        => ActualChat.Users.Captcha.IsAvailable(HostInfo, Settings.GoogleRecaptchaSiteKey);

    public async Task Require(
        Session session,
        string? token,
        string? action,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var expectedAction = Constants.Recaptcha.Actions.ForPurpose(purpose);
        if (token.IsNullOrEmpty()) {
            if (IsProofRequired && !await IsNativeAppSession(session, cancellationToken).ConfigureAwait(false))
                throw StandardError.Constraint(MissingProofMessage);

            return;
        }

        if (action != expectedAction) {
            Log.LogWarning("Captcha proof action mismatch: got '{Action}', expected '{ExpectedAction}'",
                action, expectedAction);
            throw StandardError.Constraint(InvalidProofMessage);
        }

        var result = await Captcha.Validate(token, expectedAction, cancellationToken).ConfigureAwait(false);
        if (result.Success)
            return;

        Log.LogWarning("Captcha proof for '{ExpectedAction}' isn't valid: {Error}",
            expectedAction, result.ErrorMessage);
        throw StandardError.Constraint(InvalidProofMessage);
    }

    // Private methods

    private async Task<bool> IsNativeAppSession(Session session, CancellationToken cancellationToken)
    {
        // Native apps host their UI in a WebView w/o reCAPTCHA, so they can never mint a proof;
        // their sessions are the ones created via IMobileSessions, i.e. carrying an app user agent.
        var sessionInfo = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        return AppKindExt.TryParseUserAgent(sessionInfo?.Description, out var appKind)
            && appKind != AppKind.Wasm;
    }
}
