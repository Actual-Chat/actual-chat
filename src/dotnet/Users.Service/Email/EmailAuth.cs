using System.Security.Claims;
using ActualChat.Hashing;
using ActualChat.Hosting;
using ActualChat.Resilience;
using ActualChat.Rpc;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using ActualChat.Users.Phone;
using ActualChat.Users.Templates;
using ActualLab.Redis;
using ActualLab.Rpc.Infrastructure;
using Mjml.Net;
using StackExchange.Redis;

namespace ActualChat.Users.Email;

public class EmailAuth(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IEmailAuth
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Email.TotpLength);
    private const int TestAgentTotp = 111111;

    private HostInfo HostInfo { get; } = services.HostInfo();
    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private IEmailSender EmailSender { get; } = services.GetRequiredService<IEmailSender>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private TotpCodes TotpCodes { get; } = services.GetRequiredService<TotpCodes>();
    private CaptchaProofValidator CaptchaProofs { get; } = services.GetRequiredService<CaptchaProofValidator>();
    private RateLimitPolicy RateLimitPolicy => field ??= Services.GetRequiredService<RateLimitPolicy>();
    private RedisDb<UsersDbContext> RedisDb { get; } = services.GetRequiredService<RedisDb<UsersDbContext>>();

    // [ComputeMethod]
    public virtual Task<string> GetEmailValidationMessage(
        Session session, ActualChat.Email email, TotpPurpose purpose, CancellationToken cancellationToken)
        => Task.FromResult(
            System.Net.Mail.MailAddress.TryCreate(email.Value, out _)
                ? string.Empty
                : "Invalid email address.");

    [Obsolete("2026.07: Use GetEmailValidationMessage.")]
    // [ComputeMethod]
    public virtual Task<string> CheckIfBlocked(
        Session session, ActualChat.Email email, TotpPurpose purpose, CancellationToken cancellationToken)
        => GetEmailValidationMessage(session, email, purpose, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<bool> AccountExists(
        Session session, ActualChat.Email email, CancellationToken cancellationToken)
    {
        var method = $"{nameof(EmailAuth)}.{nameof(AccountExists)}";
        var identities = new RateLimitIdentity[2];
        var identityCount = 0;
        identities[identityCount++] = new RateLimitIdentity(
            RateLimitIdentityKind.Target,
            $"{method}:{email.Value}");
        if (RateLimitIdentity.ForIP(RpcInboundContext.Current.GetRemoteIPAddress()) is { } ipIdentity)
            identities[identityCount++] = ipIdentity;
        await RateLimitPolicy
            .Check(method, RateLimitClass.Auth, identities.AsSpan(0, identityCount), cancellationToken)
            .ConfigureAwait(false);

        var identity = UserIdentityExt.NewEmailIdentity(email);
        var userId = await AccountsBackend.GetIdByUserIdentity(identity, cancellationToken).ConfigureAwait(false);
        return userId is not null;
    }

    // [CommandHandler]
    public virtual async Task<Moment> OnSendTotp(EmailAuth_SendTotp command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var purpose = command.Purpose;
        var email = command.Email.Value;

        if (Constants.Auth.TestAgent.IsTestAgentEmail(email) && IsTestAgentEmailTotpHost(HostInfo))
            return NextSendAt();

        await CaptchaProofs
            .Require(session, command.CaptchaToken, command.CaptchaAction, purpose, cancellationToken)
            .ConfigureAwait(false);

        if (await IsThrottled(session, email, cancellationToken).ConfigureAwait(false))
            return NextSendAt();

        var canSendValidationMessage = await GetEmailValidationMessage(
                session, command.Email, purpose, cancellationToken)
            .ConfigureAwait(false);
        if (!canSendValidationMessage.IsNullOrEmpty())
            throw StandardError.Constraint(canSendValidationMessage);

        var totp = await TotpCodes.Generate(purpose, email, session, cancellationToken).ConfigureAwait(false);
        var nextSendAt = NextSendAt();
        var sTotp = totp.ToString(TotpFormat);
        if (!HostInfo.IsProductionInstance)
            Log.LogWarning("!!! Email verification code for {Email}: {Code}", email, sTotp);

        var subject = purpose switch {
            TotpPurpose.SignInEmail => $"{CoreConstants.AppName}: sign-in code",
            TotpPurpose.VerifyEmail => $"{CoreConstants.AppName}: email verification",
            _ => $"{CoreConstants.AppName}: code",
        };

        var parameters = new Dictionary<string, object?>() {
            { nameof(EmailVerification.Token), sTotp },
        };
        var blazorRenderer = new BlazorRenderer();
        await using var _ = blazorRenderer.ConfigureAwait(false);
        var mjml = await blazorRenderer.RenderComponent<EmailVerification>(parameters).ConfigureAwait(false);
        var mjmlRenderer = new MjmlRenderer();
        var mjmlOptions = new MjmlOptions { Beautify = false };
        var renderResult = await mjmlRenderer
            .RenderAsync(mjml, mjmlOptions, cancellationToken)
            .ConfigureAwait(false);
        var html = renderResult.Html;

        await EmailSender
            .Send("", email, subject, html, cancellationToken)
            .ConfigureAwait(false);
        return nextSendAt;

        DateTimeOffset NextSendAt()
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

        var identities = new ApiMap<UserIdentity, string>().WithEmailIdentity(email, out var emailIdentity);
        var claims = new ApiMap<string, string>().With(ClaimTypes.Email, email.Value);

        var signInCommand = new AccountsBackend_SignIn(session, emailIdentity, identities, claims);
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnVerifyEmail(EmailAuth_VerifyEmail command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return false; // It just spawns other commands, so nothing to do here

        var (session, email, totp) = command;
        if (!await ValidateCode(session, email.Value, totp, TotpPurpose.VerifyEmail, cancellationToken).ConfigureAwait(false))
            return false;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var updatedAccount = account.WithEmailIdentity(email);
        await Accounts.AssertCanUpdate(session, updatedAccount, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var emailIdentity = UserIdentityExt.NewEmailIdentity(email);
        var conflictingUserId = await dbContext
            .GetUserIdByIdentity(emailIdentity, false, cancellationToken)
            .ConfigureAwait(false);
        if (conflictingUserId != null && conflictingUserId.Value != account.Id.Value)
            throw StandardError.Unauthorized("Email has already been taken by another account.");

        var cmd = new AccountsBackend_Update(updatedAccount, account.Version);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Protected/internal methods

    internal static bool IsTestAgentEmailTotpHost(HostInfo hostInfo)
    {
        var baseUri = hostInfo.BaseUrl.ToUri();
        return hostInfo.IsTested
            || baseUri.IsLoopback
            || Constants.Hosts.IsLocalDev(baseUri.Host);
    }

    // Private methods

    private async Task<bool> ValidateCode(
        Session session,
        string email,
        int totp,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var method = $"{nameof(EmailAuth)}.{purpose}";
        var identities = new RateLimitIdentity[2];
        var identityCount = 0;
        identities[identityCount++] = new RateLimitIdentity(RateLimitIdentityKind.Target, $"{purpose}:{email}");
        if (RateLimitIdentity.ForIP(RpcInboundContext.Current.GetRemoteIPAddress()) is { } ipIdentity)
            identities[identityCount++] = ipIdentity;
        await RateLimitPolicy
            .Check(method, RateLimitClass.Auth, identities.AsSpan(0, identityCount), cancellationToken)
            .ConfigureAwait(false);

        if (totp == TestAgentTotp
            && Constants.Auth.TestAgent.IsTestAgentEmail(email)
            && IsTestAgentEmailTotpHost(HostInfo))
            return true;

        return await TotpCodes.Validate(purpose, email, session, totp, cancellationToken).ConfigureAwait(false);
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
