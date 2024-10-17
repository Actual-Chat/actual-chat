using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public class TotpUI(UIHub hub)
{
    private readonly IMutableState<Moment> _totpExpiresAt = hub.StateFactory().NewMutable<Moment>();
    private UIHub Hub { get; } = hub;
    private Session Session => Hub.Session();
    private UICommander UICommander => Hub.UICommander();

    public IState<Moment> TotpExpiresAt => _totpExpiresAt;

    public async Task<bool> SendPhoneCode(TotpPurpose purpose, string phone, CancellationToken cancellationToken)
    {
        var cmd = purpose switch {
            TotpPurpose.SignIn or TotpPurpose.VerifyPhone => new PhoneAuth_SendTotp(Session, new Phone(phone), purpose),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
        };
        var (totpExpiresAt, error) = await UICommander.Run(cmd, cancellationToken);
        if (error != null)
            return false;

        _totpExpiresAt.Value = totpExpiresAt;
        return true;
    }

    public async Task<bool> SendEmailCode(CancellationToken cancellationToken)
    {
        var (totpExpiresAt, error) = await UICommander.Run(new Emails_SendTotp(Session), cancellationToken);
        if (error != null)
            return false;

        _totpExpiresAt.Value = totpExpiresAt;
        return true;
    }
}
