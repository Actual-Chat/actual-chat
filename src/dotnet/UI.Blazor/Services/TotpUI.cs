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
    public Task<string> CheckIfBlocked(Email email, TotpPurpose purpose, CancellationToken cancellationToken)
        => EmailAuth.CheckIfBlocked(Session, email, purpose, cancellationToken);

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
        var cmd = purpose switch {
            TotpPurpose.SignInPhone or TotpPurpose.VerifyPhone => new PhoneAuth_SendTotp(Session, phone, purpose),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
        };
        var (totpNextSendAt, error) = await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
        if (error != null)
            return false;

        _totpNextSendAt.Value = totpNextSendAt;
        return true;
    }

    public async Task<bool> SendCode(TotpPurpose purpose, Email email, CancellationToken cancellationToken)
    {
        var (totpNextSendAt, error) = await UICommander.Run(new EmailAuth_SendTotp(Session, email, purpose), cancellationToken).ConfigureAwait(false);
        if (error != null)
            return false;

        _totpNextSendAt.Value = totpNextSendAt;
        return true;
    }
}
