using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public class TotpUI(UIHub hub): ScopedServiceBase<UIHub>(hub), IComputeService
{
    private readonly IMutableState<Moment> _totpExpiresAt = hub.StateFactory().NewMutable<Moment>();
    private UICommander UICommander => Hub.UICommander();
    public IState<Moment> TotpExpiresAt => _totpExpiresAt;

    [ComputeMethod]
    public virtual async Task<bool> HasSentCodeRecently(CancellationToken cancellationToken)
    {
        var now = Hub.Clocks().ServerClock.Now;
        var expiresAt = await _totpExpiresAt.Use(cancellationToken).ConfigureAwait(false);
        var canExpire = expiresAt > now;
        if (canExpire)
            Computed.GetCurrent().Invalidate(expiresAt - now + TimeSpan.FromSeconds(1));

        return canExpire;
    }

    public async Task<bool> SendPhoneCode(TotpPurpose purpose, string phone, CancellationToken cancellationToken)
    {
        var cmd = purpose switch {
            TotpPurpose.SignIn or TotpPurpose.VerifyPhone => new PhoneAuth_SendTotp(Session, new Phone(phone), purpose),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
        };
        var (totpExpiresAt, error) = await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
        if (error != null)
            return false;

        _totpExpiresAt.Value = totpExpiresAt;
        return true;
    }

    public async Task<bool> SendEmailCode(CancellationToken cancellationToken)
    {
        var (_, error) = await UICommander.Run(new Emails_SendTotp(Session), cancellationToken).ConfigureAwait(false);
        return error == null;
    }
}
