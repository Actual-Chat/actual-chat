using System.Security.Claims;
using System.Text;
using ActualChat.Hosting;
using ActualChat.Users.Module;

namespace ActualChat.Users;

public class PhoneAuth : IPhoneAuth
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Phone.TotpLength);
    private UsersSettings Settings { get; }
    private HostInfo HostInfo { get; }
    private ISmsGateway Sms { get; }
    private Rfc6238AuthenticationService Totps { get; }
    private TotpRandomSecrets RandomSecrets { get; set; }
    private MomentClockSet Clocks { get; }
    private ICommander Commander { get; }

    public PhoneAuth(IServiceProvider services)
    {
        Settings = services.GetRequiredService<UsersSettings>();
        HostInfo = services.GetRequiredService<HostInfo>();
        Sms = services.GetRequiredService<ISmsGateway>();
        Totps = services.GetRequiredService<Rfc6238AuthenticationService>();
        RandomSecrets = services.GetRequiredService<TotpRandomSecrets>();
        Clocks = services.Clocks();
        Commander = services.Commander();
    }

    // [ComputeMethod]
    // TODO: move to Features_EnablePhoneAuth
    public virtual Task<bool> IsEnabled(CancellationToken cancellationToken)
        => Task.FromResult(HostInfo.IsDevelopmentInstance || Settings.IsTwilioEnabled);

    // [CommandHandler]
    public virtual async Task<Moment> OnSendTotp(PhoneAuth_SendTotp command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default;

        // TODO: throttle
        var (session, phone) = command;
        var (securityToken, modifier) = await GetTotpInputs(session, phone).ConfigureAwait(false);
        var totp = Totps.GenerateCode(securityToken, modifier); // generate totp with the newest one
        var expiresAt = Clocks.SystemClock.UtcNow + Settings.TotpLifetime;

        var sTotp = totp.ToString(TotpFormat, CultureInfo.InvariantCulture);
        await Sms.Send(phone, $"Your ActualChat.ID code is: {sTotp}. Don't share it with anyone.").ConfigureAwait(false);
        return expiresAt;
    }

    [CommandHandler]
    public virtual async Task<bool> OnValidateTotp(
        PhoneAuth_ValidateTotp command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default; // It just spawns other commands, so nothing to do here

        var (session, phone, totp) = command;
        var (securityToken, modifier) = await GetTotpInputs(session, phone).ConfigureAwait(false);

        // checking previous security token in case secret rotation happened
        if (!Totps.ValidateCode(securityToken, totp, modifier))
            return false;

        var user = new User(Symbol.Empty, string.Empty)
            .WithIdentity(new UserIdentity(Constants.Auth.Phone.SchemeName, phone.Value))
            .WithClaim(ClaimTypes.MobilePhone, phone);
        await Commander
            .Call(new AuthBackend_SignIn(session, user), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<(byte[] SecurityToken, string Modifier)> GetTotpInputs(Session session, Phone phone)
    {
        var randomSecret = await RandomSecrets.Get(session).ConfigureAwait(false);
        var securityTokens = Encoding.UTF8.GetBytes($"{randomSecret}_{session.Id}_{phone}");
        var modifier = $"SignIn:{phone}";
        return (securityTokens, modifier);
    }
}
