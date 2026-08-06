using System.Security.Claims;
using ActualChat.Hashing;
using ActualChat.Resilience;
using ActualChat.Rpc;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Redis;
using ActualLab.Rpc.Infrastructure;
using StackExchange.Redis;

namespace ActualChat.Users.Phone;

public class PhoneAuth : DbServiceBase<UsersDbContext>, IPhoneAuth
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Phone.TotpLength);

    private UsersSettings Settings { get; }
    private HostInfo HostInfo { get; }
    private ITextMessageSender TextMessage { get; }
    private TotpCodes Totps { get; }
    private CaptchaProofValidator CaptchaProofs { get; }
    private RateLimitPolicy RateLimitPolicy { get; }
    private RedisDb<UsersDbContext> RedisDb { get; }
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private string[] BlockedPhonePrefixes
        => field ??= Settings.BlockedPhonePrefixes.Split([';', ','], StringSplitOptions.RemoveEmptyEntries);

    public PhoneAuth(IServiceProvider services) : base(services)
    {
        Settings = services.GetRequiredService<UsersSettings>();
        HostInfo = services.HostInfo();
        TextMessage = services.GetRequiredService<ITextMessageSender>();
        Totps = services.GetRequiredService<TotpCodes>();
        CaptchaProofs = services.GetRequiredService<CaptchaProofValidator>();
        RateLimitPolicy = services.GetRequiredService<RateLimitPolicy>();
        RedisDb = services.GetRequiredService<RedisDb<UsersDbContext>>();
    }

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled(CancellationToken cancellationToken)
        => Task.FromResult(HostInfo.IsDevelopmentInstance || Settings.IsTwilioEnabled || Settings.IsSMSToEnabled);

    // [ComputeMethod]
    public virtual Task<string> CheckIfBlocked(
        Session session,
        ActualChat.Phone phone,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var value = phone.Normalize().Value;
        if (!IsBlocked())
            return Task.FromResult(string.Empty);

        var message = purpose switch {
            TotpPurpose.SignInPhone => "Unable to send SMS to this number, please use other login methods.",
            _ => "Unable to send SMS to this number",
        };
        return Task.FromResult(message);

        bool IsBlocked() {
            foreach (var blockedPrefix in BlockedPhonePrefixes)
                if (value.StartsWith(blockedPrefix))
                    return true;
            return false;
        }
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
    public virtual async Task<Moment> OnSendTotp(PhoneAuth_SendTotp command, CancellationToken cancellationToken)
    {
        // NOTE(AY): A bit suspicious IApiCommand design:
        // - On one hand, it doesn't have to invalidate anything
        // - On another, it doesn't use a backend.
        if (Invalidation.IsActive)
            return default; // It just spawns other commands, so nothing to do here

        var (session, phone, purpose, captchaToken, captchaAction) = command;
        if (TryGetPredefined(phone, out _))
            return NextSendAt(); // no need to send predefined totp

        await CaptchaProofs
            .Require(session, captchaToken, captchaAction, purpose, cancellationToken)
            .ConfigureAwait(false);

        // Throttle the send rate: limit by phone and by session
        if (await IsThrottled(session, phone, cancellationToken).ConfigureAwait(false))
            return NextSendAt();

        var canSendValidationMessage = await CheckIfBlocked(session, phone, purpose, cancellationToken).ConfigureAwait(false);
        if (!canSendValidationMessage.IsNullOrEmpty())
            throw StandardError.Constraint(canSendValidationMessage);

        var totp = await Totps.Generate(purpose, phone.Value, session, cancellationToken).ConfigureAwait(false);
        var nextSendAt = NextSendAt();
        var sTotp = totp.ToString(TotpFormat);
        if (!HostInfo.IsProductionInstance)
            Log.LogWarning("!!! Phone verification code for {Phone}: {Code}", phone.Value, sTotp);
        await TextMessage.Send(phone, $"{CoreConstants.AppName}: your phone verification code is {sTotp}. Don't share it with anyone.").ConfigureAwait(false);
        return nextSendAt;

        DateTimeOffset NextSendAt()
            => Clocks.SystemClock.UtcNow + Settings.TotpUIThrottling;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnValidateTotp(
        PhoneAuth_ValidateTotp command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return false; // It just spawns other commands, so nothing to do here

        var (session, phone, totp) = command;
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
        if (Invalidation.IsActive)
            return false; // It just spawns other commands, so nothing to do here

        var (session, phone, totp) = command;
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
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var phoneIdentity = UserIdentityExt.NewPhoneIdentity(phone);
        var conflictingUserId = await dbContext
            .GetUserIdByIdentity(phoneIdentity, false, cancellationToken)
            .ConfigureAwait(false);
        if (conflictingUserId != null && conflictingUserId.Value != account.Id.Value)
            throw StandardError.Unauthorized("Phone number has already been taken by another account.");

        var cmd = new AccountsBackend_Update(updatedAccount, account.Version);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> IsThrottled(Session session, ActualChat.Phone phone, CancellationToken cancellationToken)
    {
        // Fixed-window throttle using a single Redis call per scope (SET NX with TTL)
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var window = Settings.TotpUIThrottling;

        var phoneKey = $".SmsTotpThrottle:phone:{Hash(phone.E164Value)}";
        var sessionKey = $".SmsTotpThrottle:session:{Hash(session.Id)}";

        // true => first request in window; false => already requested (throttled)
        var phoneOk = await db.StringSetAsync(phoneKey, "1", window, When.NotExists).ConfigureAwait(false);
        var sessionOk = await db.StringSetAsync(sessionKey, "1", window, When.NotExists).ConfigureAwait(false);

        // Throttle if either phone or session key already exists in the window
        return !(phoneOk && sessionOk);
    }

    private static string Hash(string value)
        => value
            .Hash()
            .SHA256()
            .ToBase64HashString(HashAlgorithm.SHA256);

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

        if (TryGetPredefined(phone, out var predefinedTotp))
            return predefinedTotp == totp;

        return await Totps.Validate(purpose, phone.Value, session, totp, cancellationToken).ConfigureAwait(false);
    }

    private bool TryGetPredefined(ActualChat.Phone phone, out int predefinedTotp)
        // removing dashes due to issue with dash in bash env var names
        => Settings.PredefinedTotps.TryGetValue(ActualChat.Phone.NormalizePart(phone.Value), out predefinedTotp);
}
