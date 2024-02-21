using System.Text;
using ActualChat.Blazor;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using ActualChat.Users.Templates;

namespace ActualChat.Users.Email;

public class Emails(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IEmails
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Email.TotpLength);

    private IAuthBackend? _authBackend;
    private IAccounts? _accounts;
    private IEmailSender? _emailSender;
    private UsersSettings? _settings;
    private Rfc6238AuthenticationService? _totps;
    private TotpRandomSecrets? _randomSecrets;

    private UsersSettings Settings => _settings ??= Services.GetRequiredService<UsersSettings>();
    private IEmailSender EmailSender => _emailSender ??= Services.GetRequiredService<IEmailSender>();
    private Rfc6238AuthenticationService Totps => _totps ??= Services.GetRequiredService<Rfc6238AuthenticationService>();
    private TotpRandomSecrets RandomSecrets => _randomSecrets ??= Services.GetRequiredService<TotpRandomSecrets>();
    private IAccounts Accounts => _accounts ??= Services.GetRequiredService<IAccounts>();
    private IAuthBackend AuthBackend => _authBackend ??= Services.GetRequiredService<IAuthBackend>();

    // [CommandHandler]
    public virtual async Task<Moment> OnSendTotp(Emails_SendTotp command, CancellationToken cancellationToken) {
        if (Computed.IsInvalidating())
            return default;

        var session = command.Session;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var email = account.Email;
        var (securityToken, modifier) = await GetTotpInputs(session, email, TotpPurpose.VerifyEmail, cancellationToken).ConfigureAwait(false);
        var totp = Totps.GenerateCode(securityToken, modifier); // generate totp with the newest one
        var expiresAt = GetExpiresAt();

        var sTotp = totp.ToString(TotpFormat, CultureInfo.InvariantCulture);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) {
            { nameof(EmailVerification.Token), sTotp },
        };
        var renderer = new BlazorRenderer();
        await using var _ = renderer.ConfigureAwait(false);
        var html = await renderer.RenderComponent<EmailVerification>(parameters).ConfigureAwait(false);
        await EmailSender.Send(
                "",
                email,
                "Actual Chat: email verification",
                html,
                cancellationToken)
            .ConfigureAwait(false);
        return expiresAt;

        DateTimeOffset GetExpiresAt()
            => Clocks.SystemClock.UtcNow + Settings.TotpUIThrottling;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnVerifyEmail(Emails_VerifyEmail command, CancellationToken cancellationToken) {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            // TODO(AY): support UserId (any non-string/non-int) type for multi-instance deployment
            var userId = context.Operation().Items.GetId<UserId>();
            if (!userId.IsNone)
                _ = AuthBackend.GetUser(default, userId, cancellationToken);
            return default;
        }

        var (session, totp) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!await ValidateCode(session, account.Email, totp, TotpPurpose.VerifyEmail, cancellationToken).ConfigureAwait(false))
            return false;

        account = account with { IsEmailVerified = true };
        await Accounts.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);
        var cmd = new AccountsBackend_Update(account, account.Version);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);

        context.Operation().Items.SetId(account.Id);
        return true;
    }

    private async Task<bool> ValidateCode(
        Session session,
        string email,
        int totp,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var (securityToken, modifier) = await GetTotpInputs(session, email, purpose, cancellationToken).ConfigureAwait(false);
        return Totps.ValidateCode(securityToken, totp, modifier);
    }

    private async Task<(byte[] SecurityToken, string Modifier)> GetTotpInputs(
        Session session,
        string email,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var randomSecret = await RandomSecrets.Get(session, cancellationToken).ConfigureAwait(false);
        var securityTokens = Encoding.UTF8.GetBytes($"{randomSecret}_{session.Id}_{email}");
        var modifier = $"{purpose}:{email}";
        return (securityTokens, modifier);
    }
}
