namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages time-based one-time password (TOTP) code sending for phone and email verification.
/// </summary>
public class TotpUI(UIHub hub): UIServiceBase<UIHub>(hub), IComputeService
{
    private readonly MutableState<Moment> _totpNextSendAt = hub.StateFactory.NewMutable<Moment>();

    private IPhoneAuth PhoneAuth => field ??= Services.GetRequiredService<IPhoneAuth>();
    private IEmailAuth EmailAuth => field ??= Services.GetRequiredService<IEmailAuth>();

    public IState<Moment> TotpNextSendAt => _totpNextSendAt;

    public void Reset()
        => _totpNextSendAt.Value = default;

    public Task<string> CheckIfBlocked(Phone phone, TotpPurpose purpose, CancellationToken cancellationToken)
        => PhoneAuth.CheckIfBlocked(Session, phone, purpose, cancellationToken);
    public Task<string> GetEmailValidationMessage(
        Email email, TotpPurpose purpose, CancellationToken cancellationToken)
        => EmailAuth.GetEmailValidationMessage(Session, email, purpose, cancellationToken);

    public Task<bool> AccountExists(Phone phone, CancellationToken cancellationToken)
        => PhoneAuth.AccountExists(Session, phone, cancellationToken);
    public Task<bool> AccountExists(Email email, CancellationToken cancellationToken)
        => EmailAuth.AccountExists(Session, email, cancellationToken);

    [ComputeMethod]
    public virtual async Task<bool> HasSentCodeRecently(CancellationToken cancellationToken)
    {
        var now = Clocks.ServerClock.Now;
        var nextSendAt = await _totpNextSendAt.Use(cancellationToken).ConfigureAwait(false);
        var hasSentRecently = nextSendAt > now;
        if (hasSentRecently)
            Computed.GetCurrent().Invalidate(nextSendAt - now + TimeSpan.FromSeconds(1));
        return hasSentRecently;
    }

    public async Task<bool> SendCode(TotpPurpose purpose, Phone phone, CancellationToken cancellationToken)
    {
        if (purpose is not (TotpPurpose.SignInPhone or TotpPurpose.VerifyPhone))
            throw new ArgumentOutOfRangeException(nameof(purpose));

        var (token, action) = await GetCaptchaProof(purpose, cancellationToken).ConfigureAwait(false);
        var cmd = new PhoneAuth_SendTotp(Session, phone, purpose, token, action);
        var (totpNextSendAt, error) = await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
        if (error != null)
            return false;

        _totpNextSendAt.Value = totpNextSendAt;
        return true;
    }

    public async Task<bool> SendCode(TotpPurpose purpose, Email email, CancellationToken cancellationToken)
    {
        var (token, action) = await GetCaptchaProof(purpose, cancellationToken).ConfigureAwait(false);
        var cmd = new EmailAuth_SendTotp(Session, email, purpose, token, action);
        var (totpNextSendAt, error) = await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
        if (error != null)
            return false;

        _totpNextSendAt.Value = totpNextSendAt;
        return true;
    }

    // Private methods

    private async Task<(string? Token, string? Action)> GetCaptchaProof(
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        // A fresh token per send: captcha tokens are single-use, so resends can't share one.
        var captchaUI = Hub.CaptchaUI;
        await captchaUI.WhenReady.SilentAwait(false);
        if (!captchaUI.IsAvailable)
            return default;

        var action = Constants.Recaptcha.Actions.ForPurpose(purpose);
        try {
            var token = await captchaUI.GetActionToken(action, cancellationToken).ConfigureAwait(false);
            return token.IsNullOrEmpty() ? default : (token, action);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            Log.LogWarning(e, "Couldn't get a captcha token for '{Action}'", action);
            return default;
        }
    }
}
