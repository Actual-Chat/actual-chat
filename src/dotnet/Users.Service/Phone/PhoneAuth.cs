using System.Security.Claims;
using ActualChat.Resilience;
using ActualChat.Rpc;
using ActualChat.Users.Module;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Users.Phone;

public class PhoneAuth : IPhoneAuth
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Phone.TotpLength);

    private IServiceProvider Services { get; }
    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }
    private UsersSettings Settings { get; }
    private HostInfo HostInfo { get; }
    private IVerificationCodeSender CodeSender { get; }
    private ITotpCodesBackend Totps { get; }
    private CaptchaProofValidator CaptchaProofs { get; }
    private RateLimitPolicy RateLimitPolicy { get; }
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private ILogger Log => field ??= Services.LogFor(GetType());
    private string[] BlockedPhonePrefixes
        => field ??= Settings.BlockedPhonePrefixes.Split([';', ','], StringSplitOptions.RemoveEmptyEntries);

    public PhoneAuth(IServiceProvider services)
    {
        Settings = services.GetRequiredService<UsersSettings>();
        HostInfo = services.HostInfo();
        CodeSender = services.GetRequiredService<IVerificationCodeSender>();
        Totps = services.GetRequiredService<ITotpCodesBackend>();
        CaptchaProofs = services.GetRequiredService<CaptchaProofValidator>();
        RateLimitPolicy = services.GetRequiredService<RateLimitPolicy>();
        Services = services;
        Commander = services.Commander();
        Clocks = services.Clocks();
    }

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled(CancellationToken cancellationToken)
        => Task.FromResult(HostInfo.IsDevelopmentInstance
            || Settings.IsTelegramGatewayEnabled
            || Settings.IsTwilioEnabled
            || Settings.IsSMSToEnabled);

    // [ComputeMethod]
    public virtual Task<string> CheckIfBlocked(
        Session session,
        ActualChat.Phone phone,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        // A blocked prefix only rules out SMS - Telegram still reaches the number, so the flow is
        // refused only when that way out is closed too
        if (!IsSmsBlocked(phone) || Settings.IsTelegramGatewayEnabled)
            return Task.FromResult(string.Empty);

        var message = purpose switch {
            TotpPurpose.SignInPhone => "Unable to send SMS to this number, please use other login methods.",
            _ => "Unable to send SMS to this number",
        };

        return Task.FromResult(message);
    }

    // [ComputeMethod]
    public virtual async Task<bool> AccountExists(
        Session session,
        ActualChat.Phone phone,
        CancellationToken cancellationToken)
    {
        var method = $"{nameof(PhoneAuth)}.{nameof(AccountExists)}";
        var identities = new RateLimitIdentity[2];
        var identityCount = 0;
        identities[identityCount++] = new RateLimitIdentity(
            RateLimitIdentityKind.Target,
            $"{method}:{phone.Value}");
        if (RateLimitIdentity.ForIP(RpcInboundContext.Current.GetRemoteIPAddress()) is { } ipIdentity)
            identities[identityCount++] = ipIdentity;
        await RateLimitPolicy
            .Check(method, RateLimitClass.Auth, identities.AsSpan(0, identityCount), cancellationToken)
            .ConfigureAwait(false);

        var identity = UserIdentityExt.NewPhoneIdentity(phone);
        var userId = await AccountsBackend.GetIdByUserIdentity(identity, cancellationToken).ConfigureAwait(false);

        return userId is not null;
    }

    // [CommandHandler]
    public virtual async Task<TotpSendResult> OnSendCode(
        PhoneAuth_SendCode command,
        CancellationToken cancellationToken)
    {
        // NOTE(AY): A bit suspicious IApiCommand design:
        // - On one hand, it doesn't have to invalidate anything
        // - On another, it doesn't use a backend.
        var session = command.Session;
        var phone = command.Phone;
        var purpose = command.Purpose;
        var captchaToken = command.CaptchaToken;
        var captchaAction = command.CaptchaAction;
        if (IsTestAgentPhone(phone) || TryGetPredefined(phone, out _))
            return new TotpSendResult(NextSendAt(), null); // no need to send predefined totp

        await CaptchaProofs
            .Require(session, captchaToken, captchaAction, purpose, cancellationToken)
            .ConfigureAwait(false);

        // The throttled call reports the channel of the code that's still live, so the UI keeps naming it.
        if (await IsThrottled(phone, cancellationToken).ConfigureAwait(false))
            return new TotpSendResult(
                NextSendAt(),
                await GetLastChannel(phone, cancellationToken).ConfigureAwait(false));

        var canSendValidationMessage = await CheckIfBlocked(session, phone, purpose, cancellationToken)
            .ConfigureAwait(false);
        if (!canSendValidationMessage.IsNullOrEmpty())
            throw StandardError.Constraint(canSendValidationMessage);

        var totp = await Totps.Generate(phone.Value, purpose, cancellationToken).ConfigureAwait(false);
        var nextSendAt = NextSendAt();
        var sTotp = totp.ToString(TotpFormat);
        if (!HostInfo.IsProductionInstance)
            Log.LogWarning("!!! Phone verification code for {Phone}: {Code}", phone.Value, sTotp);

        var onlyChannel = IsSmsBlocked(phone) ? TotpChannel.Telegram : (TotpChannel?)null;
        var text = $"{CoreConstants.AppName}: your phone verification code is {sTotp}. Don't share it with anyone.";
        var sentChannel = await CodeSender
            .Send(phone, new VerificationMessage(sTotp, text, onlyChannel))
            .ConfigureAwait(false);
        if (sentChannel is { } sent)
            await SetLastChannel(phone, sent, cancellationToken).ConfigureAwait(false);

        return new TotpSendResult(nextSendAt, sentChannel);

        DateTimeOffset NextSendAt()
            => Clocks.SystemClock.UtcNow + Settings.TotpUIThrottling;
    }

    // [CommandHandler]
    [Obsolete("2026.08: Use PhoneAuth_SendCode. Old clients only.")]
    public virtual async Task<Moment> OnSendTotp(PhoneAuth_SendTotp command, CancellationToken cancellationToken)
    {
        var sendCodeCommand = new PhoneAuth_SendCode {
            Session = command.Session,
            Phone = command.Phone,
            Purpose = command.Purpose,
            CaptchaToken = command.CaptchaToken,
            CaptchaAction = command.CaptchaAction,
        };
        var result = await Commander.Call(sendCodeCommand, true, cancellationToken).ConfigureAwait(false);

        return result.NextSendAt;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnValidateTotp(
        PhoneAuth_ValidateTotp command,
        CancellationToken cancellationToken)
    {
        var session = command.Session;
        var phone = command.Phone;
        var totp = command.Totp;
        if (!await ValidateCode(session, phone, totp, TotpPurpose.SignInPhone, cancellationToken).ConfigureAwait(false))
            return false;

        var identities = new ApiMap<UserIdentity, string>().WithPhoneIdentity(phone, out var phoneIdentity);
        var claims = new ApiMap<string, string>().With(ClaimTypes.MobilePhone, phone.Value);

        var signInCommand = new AccountsBackend_SignIn(session, phoneIdentity, identities, claims);
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);

        return true;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnVerifyPhone(PhoneAuth_VerifyPhone command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var phone = command.Phone;
        var totp = command.Totp;
        if (!await ValidateCode(session, phone, totp, TotpPurpose.VerifyPhone, cancellationToken).ConfigureAwait(false))
            return false;

        // save phone to account
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        await Accounts.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);
        var updatedAccount = account.WithPhoneIdentity(phone) with {
            Phone = phone,
            IsGreetingCompleted = false,
        };

        // save phone identity + phone claim
        var phoneIdentity = UserIdentityExt.NewPhoneIdentity(phone);
        var conflictingUserId = await AccountsBackend
            .GetIdByUserIdentity(phoneIdentity, cancellationToken)
            .ConfigureAwait(false);
        if (conflictingUserId != null && conflictingUserId.Value != account.Id.Value)
            throw StandardError.Unauthorized("Phone number has already been taken by another account.");

        var cmd = new AccountsBackend_Update(updatedAccount, account.Version);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);

        return true;
    }

    // The channel is picked here rather than in the UI because old clients call the obsolete
    // PhoneAuth_SendTotp and can't ask for one - a client-side rule would repeat Telegram forever
    internal bool IsSmsBlocked(ActualChat.Phone phone)
    {
        var value = phone.Normalize().Value;
        foreach (var blockedPrefix in BlockedPhonePrefixes)
            if (value.StartsWith(blockedPrefix))
                return true;

        return false;
    }

    internal Task SetLastChannel(
        ActualChat.Phone phone, TotpChannel channel, CancellationToken cancellationToken)
        => Totps.SetLastChannel(phone, channel, cancellationToken);

    // Private methods

    private Task<TotpChannel?> GetLastChannel(
        ActualChat.Phone phone, CancellationToken cancellationToken)
        => Totps.GetLastChannel(phone, cancellationToken);

    private Task<bool> IsThrottled(ActualChat.Phone phone, CancellationToken cancellationToken)
        => Totps.IsPhoneThrottled(phone, cancellationToken);

    private async Task<bool> ValidateCode(
        Session session,
        ActualChat.Phone phone,
        int totp,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var method = $"{nameof(PhoneAuth)}.{purpose}";
        var identities = new RateLimitIdentity[2];
        var identityCount = 0;
        identities[identityCount++] = new RateLimitIdentity(RateLimitIdentityKind.Target, $"{purpose}:{phone.Value}");
        if (RateLimitIdentity.ForIP(RpcInboundContext.Current.GetRemoteIPAddress()) is { } ipIdentity)
            identities[identityCount++] = ipIdentity;
        await RateLimitPolicy
            .Check(method, RateLimitClass.Auth, identities.AsSpan(0, identityCount), cancellationToken)
            .ConfigureAwait(false);

        if (totp == Constants.Auth.TestAgent.Totp && IsTestAgentPhone(phone))
            return true;
        if (TryGetPredefined(phone, out var predefinedTotp))
            return predefinedTotp == totp;

        return await Totps.Validate(phone.Value, purpose, totp, cancellationToken).ConfigureAwait(false);
    }

    private bool IsTestAgentPhone(ActualChat.Phone phone)
        // Host-gated, unlike PredefinedTotps, which serves app-review accounts and works anywhere.
        => HostInfo.IsTestAgentTotpHost() && Constants.Auth.TestAgent.IsTestAgentPhone(phone.Value);

    private bool TryGetPredefined(ActualChat.Phone phone, out int predefinedTotp)
        // removing dashes due to issue with dash in bash env var names
        => Settings.PredefinedTotps.TryGetValue(ActualChat.Phone.NormalizePart(phone.Value), out predefinedTotp);
}
