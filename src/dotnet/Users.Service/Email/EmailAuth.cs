using System.Text;
using ActualChat.Hashing;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using ActualChat.Users.Phone;
using ActualChat.Users.Templates;
using ActualLab.Fusion.Authentication.Services;
using ActualLab.Redis;
using Mjml.Net;
using StackExchange.Redis;

namespace ActualChat.Users.Email;

public class EmailAuth(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IEmailAuth
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Email.TotpLength);

    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private IEmailSender EmailSender { get; } = services.GetRequiredService<IEmailSender>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAuthBackend AuthBackend { get; } = services.GetRequiredService<IAuthBackend>();
    private TotpCodes TotpCodes { get; } = services.GetRequiredService<TotpCodes>();
    private TotpSecrets TotpSecrets { get; } = services.GetRequiredService<TotpSecrets>();
    private RedisDb<UsersDbContext> RedisDb { get; } = services.GetRequiredService<RedisDb<UsersDbContext>>();
    private IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; } = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();
    [field: AllowNull, MaybeNull]
    private IDbEntityConverter<DbUser, User> UserConverter => field ??= Services.DbEntityConverter<DbUser, User>();

    // [ComputeMethod]
    public virtual Task<string> ValidateCanSendToEmail(Session session, ActualChat.Email email, TotpPurpose purpose, CancellationToken cancellationToken)
    {
        // Basic checks: email must be set and format must be valid
        if (email is null)
            return Task.FromResult("Email address is not specified.");
        var value = email.Value;
        if (!System.Net.Mail.MailAddress.TryCreate(value, out _))
            return Task.FromResult("Email address looks invalid.");
        return Task.FromResult(string.Empty);
    }

    // [CommandHandler]
    public virtual async Task<Moment> OnSendTotp(EmailAuth_SendTotp command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var purpose = command.Purpose;
        var email = command.Email.Value;

        if (await IsThrottled(session, email, cancellationToken).ConfigureAwait(false))
            return GetExpiresAt();

        var canSendValidationMessage = await ValidateCanSendToEmail(session, command.Email, purpose, cancellationToken).ConfigureAwait(false);
        if (!canSendValidationMessage.IsNullOrEmpty())
            throw StandardError.Constraint(canSendValidationMessage);

        var (securityToken, modifier) = await GetTotpInputs(session, email, purpose, cancellationToken).ConfigureAwait(false);
        var totp = TotpCodes.Generate(securityToken, modifier);
        var expiresAt = GetExpiresAt();

        var sTotp = totp.ToString(TotpFormat, CultureInfo.InvariantCulture);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) {
            { nameof(EmailVerification.Token), sTotp },
        };
        var blazorRenderer = new BlazorRenderer();
        await using var _ = blazorRenderer.ConfigureAwait(false);
        var mjml = await blazorRenderer.RenderComponent<EmailVerification>(parameters).ConfigureAwait(false);
        var mjmlRenderer = new MjmlRenderer();
        var mjmlOptions = new MjmlOptions { Beautify = false };
        var renderResult = mjmlRenderer.Render(mjml, mjmlOptions);
        var subject = purpose switch {
            TotpPurpose.SignInEmail => $"{CoreConstants.AppName}: sign-in code",
            TotpPurpose.VerifyEmail => $"{CoreConstants.AppName}: email verification",
            _ => $"{CoreConstants.AppName}: code",
        };
        await EmailSender.Send(
                "",
                email,
                subject,
                renderResult.Html,
                cancellationToken)
            .ConfigureAwait(false);
        return expiresAt;

        DateTimeOffset GetExpiresAt()
            => Clocks.SystemClock.UtcNow + UsersSettings.TotpUIThrottling;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnValidateTotp(EmailAuth_ValidateTotp command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return false; // It just spawns other commands, so nothing to do here

        var (session, email, totp) = command;
        if (!await ValidateCode(session, email.Value, totp, TotpPurpose.SignInEmail, cancellationToken).ConfigureAwait(false))
            return false;

        var user = new User(Symbol.Empty, string.Empty).WithEmail(email);
        var signInCommand = new AuthBackend_SignIn(session, user, user.GetEmailIdentity());
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnVerifyEmail(EmailAuth_VerifyEmail command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var userId = context.Operation.Items.KeylessGet<UserId>();
            if (userId is not null)
                _ = AuthBackend.GetUser(DbShard.Single, userId.Value, cancellationToken);
            return default;
        }

        var (session, email, totp) = command;
        if (!await ValidateCode(session, email.Value, totp, TotpPurpose.VerifyEmail, cancellationToken).ConfigureAwait(false))
            return false;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account = account with { IsEmailVerified = true };
        await Accounts.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);

        var cmd = new AccountsBackend_Update(account, account.Version);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);

        // save email identity + email claim
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUser = await DbUsers.Get(dbContext, account.Id.Value, true, cancellationToken).ConfigureAwait(false);
        if (dbUser == null)
            return default; // Should never happen, but if it somehow does, there is no extra to do in this case

        var user = account.User.WithEmail(email);
        var conflictingDbUser = await DbUsers.GetByUserIdentity(dbContext, user.GetEmailIdentity(), false, cancellationToken).ConfigureAwait(false);
        if (conflictingDbUser != null && !OrdinalEquals(conflictingDbUser.Id, dbUser.Id))
            throw StandardError.Unauthorized("Email has already been taken by another account.");

        UserConverter.UpdateEntity(user, dbUser);
        context.Operation.Items.KeylessSet(account.Id);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Private methods

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

    private async Task<bool> IsThrottled(Session session, string email, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var window = UsersSettings.TotpUIThrottling;
        var emailKey = $".EmailTotpThrottle:email:{Hash(email)}";
        var sessionKey = $".EmailTotpThrottle:session:{Hash(session.Id)}";
        var emailOk = await db.StringSetAsync(emailKey, "1", window, When.NotExists).ConfigureAwait(false);
        var sessionOk = await db.StringSetAsync(sessionKey, "1", window, When.NotExists).ConfigureAwait(false);
        return !(emailOk && sessionOk);
    }

    private static string Hash(string value)
        => value
            .Hash()
            .SHA256()
            .ToBase64HashString(HashAlgorithm.SHA256);
}
