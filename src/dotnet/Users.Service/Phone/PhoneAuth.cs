using System.Text;
using ActualChat.Hashing;
using ActualChat.Hosting;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.Authentication.Services;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Users.Phone;

public class PhoneAuth : DbServiceBase<UsersDbContext>, IPhoneAuth
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Phone.TotpLength);
    private readonly Lazy<string[]> _blockedPhonePrefixes;

    private UsersSettings Settings { get; }
    private HostInfo HostInfo { get; }
    private ITextMessageSender TextMessage { get; }
    private TotpCodes Totps { get; }
    private TotpSecrets TotpSecrets { get; }
    private RedisDb<UsersDbContext> RedisDb { get; }
    private IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; }
    [field: AllowNull, MaybeNull]
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    [field: AllowNull, MaybeNull]
    private IAuthBackend AuthBackend => field ??= Services.GetRequiredService<IAuthBackend>();
    [field: AllowNull, MaybeNull]
    private IDbEntityConverter<DbUser, User> UserConverter => field ??= Services.DbEntityConverter<DbUser, User>();

    public PhoneAuth(IServiceProvider services) : base(services)
    {
        Settings = services.GetRequiredService<UsersSettings>();
        HostInfo = services.HostInfo();
        TextMessage = services.GetRequiredService<ITextMessageSender>();
        Totps = services.GetRequiredService<TotpCodes>();
        TotpSecrets = services.GetRequiredService<TotpSecrets>();
        RedisDb = services.GetRequiredService<RedisDb<UsersDbContext>>();
        DbUsers = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();
        _blockedPhonePrefixes = new Lazy<string[]>(()
            => Settings.BlockedPhonePrefixes.Split([';', ','], StringSplitOptions.RemoveEmptyEntries));
    }

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled(CancellationToken cancellationToken)
        => Task.FromResult(HostInfo.IsDevelopmentInstance || Settings.IsTwilioEnabled || Settings.IsSMSToEnabled);

    // [ComputeMethod]
    public virtual Task<string> ValidateCanSendToPhone(
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
            foreach (var blockedPrefix in _blockedPhonePrefixes.Value)
                if (value.OrdinalStartsWith(blockedPrefix))
                    return true;
            return false;
        }
    }

    // [CommandHandler]
    public virtual async Task<Moment> OnSendTotp(PhoneAuth_SendTotp command, CancellationToken cancellationToken)
    {
        // NOTE(AY): A bit suspicious IApiCommand design:
        // - On one hand, it doesn't have to invalidate anything
        // - On another, it doesn't use a backend.
        if (Invalidation.IsActive)
            return default; // It just spawns other commands, so nothing to do here

        var (session, phone, purpose) = command;
        if (TryGetPredefined(phone, out _))
            return GetExpiresAt(); // no need to send predefined totp

        // Throttle to prevent SMS pumping: limit by phone and by session
        if (await IsThrottled(session, phone, cancellationToken).ConfigureAwait(false))
            return GetExpiresAt();

        var (securityToken, modifier) = await GetTotpInputs(session, phone, purpose, cancellationToken).ConfigureAwait(false);
        var totp = Totps.Generate(securityToken, modifier); // generate totp with the newest one
        var expiresAt = GetExpiresAt();

        var canSendValidationMessage = await ValidateCanSendToPhone(session, phone, purpose, cancellationToken).ConfigureAwait(false);
        if (!canSendValidationMessage.IsNullOrEmpty())
            throw StandardError.Constraint(canSendValidationMessage);

        var sTotp = totp.ToString(TotpFormat, CultureInfo.InvariantCulture);
        await TextMessage.Send(phone, $"{CoreConstants.AppName}: your phone verification code is {sTotp}. Don't share it with anyone.").ConfigureAwait(false);
        return expiresAt;

        DateTimeOffset GetExpiresAt()
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

        var user = new User(Symbol.Empty, string.Empty).WithPhone(phone);
        var signInCommand = new AuthBackend_SignIn(session, user, user.GetPhoneIdentity());
        await Commander.Call(signInCommand, true, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnVerifyPhone(PhoneAuth_VerifyPhone command, CancellationToken cancellationToken)
    {
        // NOTE(AY): Add backend, implement IApiCommand
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            // TODO(AY): support UserId (any non-string/non-int) type for multi-instance deployment
            var userId = context.Operation.Items.KeylessGet<UserId>();
            if (userId is not null)
                _ = AuthBackend.GetUser(DbShard.Single, userId.Value, cancellationToken);
            return default;
        }

        var (session, phone, totp) = command;
        if (! await ValidateCode(session, phone, totp, TotpPurpose.VerifyPhone, cancellationToken).ConfigureAwait(false))
            return false;

        // save phone to account
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account = account with { Phone = phone, IsGreetingCompleted = false };
        await Accounts.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);

        var cmd = new AccountsBackend_Update(account, account.Version);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);

        // save phone identity + phone claim
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUser = await DbUsers.Get(dbContext, account.Id.Value, true, cancellationToken).ConfigureAwait(false);
        if (dbUser == null)
            return default; // Should never happen, but if it somehow does, there is no extra to do in this case

        var user = account.User.WithPhone(phone);
        var conflictingDbUser = await DbUsers.GetByUserIdentity(dbContext, user.GetPhoneIdentity(), false, cancellationToken).ConfigureAwait(false);
        if (conflictingDbUser != null && !OrdinalEquals(conflictingDbUser.Id, dbUser.Id))
            throw StandardError.Unauthorized("Phone number has already been taken by another account.");

        UserConverter.UpdateEntity(user, dbUser);
        context.Operation.Items.KeylessSet(account.Id);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        if (TryGetPredefined(phone, out var predefinedTotp) && predefinedTotp == totp)
            return true;

        var (securityToken, modifier) = await GetTotpInputs(session, phone, purpose, cancellationToken).ConfigureAwait(false);
        return Totps.Validate(securityToken, totp, modifier);
    }

    private async Task<(byte[] SecurityToken, string Modifier)> GetTotpInputs(
        Session session,
        ActualChat.Phone phone,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var randomSecret = await TotpSecrets.Get(session, cancellationToken).ConfigureAwait(false);
        var securityTokens = Encoding.UTF8.GetBytes($"{randomSecret}_{session.Id}_{phone}");
        var modifier = $"{purpose}:{phone}";
        return (securityTokens, modifier);
    }

    private bool TryGetPredefined(ActualChat.Phone phone, out int predefinedTotp)
        // removing dashes due to issue with dash in bash env var names
        => Settings.PredefinedTotps.TryGetValue(ActualChat.Phone.NormalizePart(phone.Value), out predefinedTotp);
}
