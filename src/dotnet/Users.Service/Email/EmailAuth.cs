using System.Security.Claims;
using ActualChat.Localization;
using ActualChat.Resilience;
using ActualChat.Rpc;
using ActualChat.Users.Module;
using ActualChat.Users.Templates;
using ActualLab.Rpc.Infrastructure;
using Mjml.Net;

namespace ActualChat.Users.Email;

public class EmailAuth(IServiceProvider services) : IEmailAuth
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Email.TotpLength);

    private IServiceProvider Services { get; } = services;
    private ICommander Commander { get; } = services.Commander();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private HostInfo HostInfo { get; } = services.HostInfo();
    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private IEmailSender EmailSender { get; } = services.GetRequiredService<IEmailSender>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private ITotpCodesBackend TotpCodes { get; } = services.GetRequiredService<ITotpCodesBackend>();
    private CaptchaProofValidator CaptchaProofs { get; } = services.GetRequiredService<CaptchaProofValidator>();
    private RateLimitPolicy RateLimitPolicy => field ??= Services.GetRequiredService<RateLimitPolicy>();
    private UserLocalizers UserLocalizers => field ??= Services.GetRequiredService<UserLocalizers>();
    private ILogger Log => field ??= Services.LogFor(GetType());

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
        var session = command.Session;
        var purpose = command.Purpose;
        var email = command.Email.Value;

        if (Constants.Auth.TestAgent.IsTestAgentEmail(email) && HostInfo.IsTestAgentTotpHost())
            return NextSendAt();
        if (GetPredefinedTotpPrefix(email) is not null)
            return NextSendAt();

        await CaptchaProofs
            .Require(session, command.CaptchaToken, command.CaptchaAction, purpose, cancellationToken)
            .ConfigureAwait(false);

        if (await IsThrottled(email, cancellationToken).ConfigureAwait(false))
            return NextSendAt();

        var canSendValidationMessage = await GetEmailValidationMessage(
                session, command.Email, purpose, cancellationToken)
            .ConfigureAwait(false);
        if (!canSendValidationMessage.IsNullOrEmpty())
            throw StandardError.Constraint(canSendValidationMessage);

        var totp = await TotpCodes.Generate(email, purpose, cancellationToken).ConfigureAwait(false);
        var nextSendAt = NextSendAt();
        var sTotp = totp.ToString(TotpFormat);
        if (!HostInfo.IsProductionInstance)
            Log.LogWarning("!!! Email verification code for {Email}: {Code}", email, sTotp);

        // A guest's settings carry the language their device resolved to, so this covers the sign-in mail too
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var l = await UserLocalizers.Get(account.Id, cancellationToken).ConfigureAwait(false);
        var subject = purpose == TotpPurpose.SignInEmail
            ? l.EmailCode_SignInSubject_Format(CoreConstants.AppName)
            : l.EmailCode_VerifySubject_Format(CoreConstants.AppName);

        var parameters = new Dictionary<string, object?>() {
            { nameof(EmailVerification.Token), sTotp },
        };
        var blazorRenderer = new BlazorRenderer(l);
        await using var _ = blazorRenderer.ConfigureAwait(false);
        var mjml = await blazorRenderer.RenderComponent<EmailVerification>(parameters).ConfigureAwait(false);
        var mjmlRenderer = new MjmlRenderer();
        var mjmlOptions = new MjmlOptions { Beautify = false };
        var renderResult = await mjmlRenderer
            .RenderAsync(mjml, mjmlOptions, cancellationToken)
            .ConfigureAwait(false);
        var html = renderResult.Html;

        await EmailSender
            .Send("", email, subject, html, null, cancellationToken)
            .ConfigureAwait(false);
        return nextSendAt;

        DateTimeOffset NextSendAt()
            => Clocks.SystemClock.UtcNow + UsersSettings.TotpUIThrottling;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnValidateTotp(EmailAuth_ValidateTotp command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var email = command.Email;
        var totp = command.Totp;
        var isValid = await ValidateCode(session, email.Value, totp, TotpPurpose.SignInEmail, cancellationToken)
            .ConfigureAwait(false);
        if (!isValid)
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
        var session = command.Session;
        var email = command.Email;
        var totp = command.Token;
        var isValid = await ValidateCode(session, email.Value, totp, TotpPurpose.VerifyEmail, cancellationToken)
            .ConfigureAwait(false);
        if (!isValid)
            return false;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var updatedAccount = account.WithEmailIdentity(email);
        await Accounts.AssertCanUpdate(session, updatedAccount, cancellationToken).ConfigureAwait(false);

        var emailIdentity = UserIdentityExt.NewEmailIdentity(email);
        var conflictingUserId = await AccountsBackend
            .GetIdByUserIdentity(emailIdentity, cancellationToken)
            .ConfigureAwait(false);
        if (conflictingUserId != null && conflictingUserId.Value != account.Id.Value)
            throw StandardError.Unauthorized("Email has already been taken by another account.");

        var cmd = new AccountsBackend_Update(updatedAccount, account.Version);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Protected/internal methods

    internal static bool IsPredefinedTotpHost(HostInfo hostInfo)
        => hostInfo.IsTested || hostInfo.BaseUrlKind is BaseUrlKind.Development or BaseUrlKind.Local;

    internal static string? GetPredefinedTotpPrefix(IReadOnlyDictionary<string, int> predefinedTotps, string email)
    {
        // The <prefix>@actual.chat mailbox itself isn't matched: only <prefix>+<suffix>@actual.chat is
        var emailSuffix = Constants.Team.EmailSuffix;
        if (predefinedTotps.Count == 0 || !email.EndsWith(emailSuffix, StringComparison.OrdinalIgnoreCase))
            return null;

        var localPart = email[..^emailSuffix.Length];
        var plusIndex = localPart.IndexOf('+');
        if (plusIndex <= 0 || plusIndex == localPart.Length - 1)
            return null;

        var prefix = localPart[..plusIndex].ToLower();
        return predefinedTotps.ContainsKey(prefix) ? prefix : null;
    }

    // Private methods

    private async Task<bool> ValidateCode(
        Session session,
        string email,
        int totp,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var predefinedTotpPrefix = GetPredefinedTotpPrefix(email);
        // All suffixes of a predefined prefix share one budget, otherwise each new suffix would reset it
        var target = predefinedTotpPrefix is null
            ? email
            : $"{predefinedTotpPrefix}+*{Constants.Team.EmailSuffix}";
        var method = $"{nameof(EmailAuth)}.{purpose}";
        var identities = new RateLimitIdentity[2];
        var identityCount = 0;
        identities[identityCount++] = new RateLimitIdentity(RateLimitIdentityKind.Target, $"{purpose}:{target}");
        if (RateLimitIdentity.ForIP(RpcInboundContext.Current.GetRemoteIPAddress()) is { } ipIdentity)
            identities[identityCount++] = ipIdentity;
        await RateLimitPolicy
            .Check(method, RateLimitClass.Auth, identities.AsSpan(0, identityCount), cancellationToken)
            .ConfigureAwait(false);

        if (totp == Constants.Auth.TestAgent.Totp
            && Constants.Auth.TestAgent.IsTestAgentEmail(email)
            && HostInfo.IsTestAgentTotpHost())
            return true;
        if (predefinedTotpPrefix is not null)
            return totp == UsersSettings.PredefinedEmailTotps[predefinedTotpPrefix];

        return await TotpCodes.Validate(email, purpose, totp, cancellationToken).ConfigureAwait(false);
    }

    private string? GetPredefinedTotpPrefix(string email)
        => IsPredefinedTotpHost(HostInfo)
            ? GetPredefinedTotpPrefix(UsersSettings.PredefinedEmailTotps, email)
            : null;

    private Task<bool> IsThrottled(string email, CancellationToken cancellationToken)
        => TotpCodes.IsEmailThrottled(email, cancellationToken);
}
