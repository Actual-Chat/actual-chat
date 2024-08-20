using System.Text;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using ActualChat.Users.Templates;
using Mjml.Net;

namespace ActualChat.Users.Email;

public class Emails(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IEmails
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Email.TotpLength);

    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private IEmailSender EmailSender { get; } = services.GetRequiredService<IEmailSender>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAuthBackend AuthBackend { get; } = services.GetRequiredService<IAuthBackend>();
    private TotpCodes TotpCodes { get; } = services.GetRequiredService<TotpCodes>();
    private TotpSecrets TotpSecrets { get; } = services.GetRequiredService<TotpSecrets>();

    // [CommandHandler]
    public virtual async Task<Moment> OnSendTotp(Emails_SendTotp command, CancellationToken cancellationToken)
    {
        // NOTE(AY): A bit suspicious IApiCommand design:
        // - On one hand, it doesn't have to invalidate anything
        // - On another, it doesn't use a backend.

        var session = command.Session;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var email = account.Email;
        var (securityToken, modifier) = await GetTotpInputs(session, email, TotpPurpose.VerifyEmail, cancellationToken).ConfigureAwait(false);
        var totp = TotpCodes.Generate(securityToken, modifier); // generate totp with the newest one
        var expiresAt = GetExpiresAt();

        var sTotp = totp.ToString(TotpFormat, CultureInfo.InvariantCulture);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) {
            { nameof(EmailVerification.Token), sTotp },
        };
        var blazorRenderer = new BlazorRenderer();
        await using var _ = blazorRenderer.ConfigureAwait(false);
        var mjml = await blazorRenderer.RenderComponent<EmailVerification>(parameters).ConfigureAwait(false);
        var mjmlRenderer = new MjmlRenderer();
        var mjmlOptions = new MjmlOptions {
            Minify = true,
            Beautify = false,
        };
        var renderResult = mjmlRenderer.Render(mjml, mjmlOptions);
        await EmailSender.Send(
                "",
                email,
                "Actual Chat: email verification",
                renderResult.Html,
                cancellationToken)
            .ConfigureAwait(false);
        return expiresAt;

        DateTimeOffset GetExpiresAt()
            => Clocks.SystemClock.UtcNow + UsersSettings.TotpUIThrottling;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnVerifyEmail(Emails_VerifyEmail command, CancellationToken cancellationToken)
    {
        // NOTE(AY): Add backend, implement IApiCommand

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            // TODO(AY): support UserId (any non-string/non-int) type for multi-instance deployment
            var userId = context.Operation.Items.GetOrDefault<UserId>();
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

        context.Operation.Items.Set(account.Id);
        return true;
    }

    // [CommandHandler]
    public virtual async Task OnSendDigest(Emails_SendDigest command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        var cmd = new EmailsBackend_SendDigest(account.Id);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ValidateCode(
        Session session,
        string email,
        int totp,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var (securityToken, modifier) = await GetTotpInputs(session, email, purpose, cancellationToken).ConfigureAwait(false);
        return TotpCodes.Validate(securityToken, totp, modifier);
    }

    private async Task<(byte[] SecurityToken, string Modifier)> GetTotpInputs(
        Session session,
        string email,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var randomSecret = await TotpSecrets.Get(session, cancellationToken).ConfigureAwait(false);
        var securityTokens = Encoding.UTF8.GetBytes($"{randomSecret}_{session.Id}_{email}");
        var modifier = $"{purpose}:{email}";
        return (securityTokens, modifier);
    }
}
