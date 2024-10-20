using System.Text;
using ActualChat.Hosting;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.Authentication.Services;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class PhoneAuth(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IPhoneAuth
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Phone.TotpLength);

    private IAuthBackend? _authBackend;
    private IAccounts? _accounts;
    private IDbEntityConverter<DbUser, User>? _userConverter;

    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private HostInfo HostInfo { get; } = services.HostInfo();
    private ITextMessageSender TextMessage { get; } = services.GetRequiredService<ITextMessageSender>();
    private TotpCodes Totps { get; } = services.GetRequiredService<TotpCodes>();
    private TotpSecrets TotpSecrets { get; } = services.GetRequiredService<TotpSecrets>();
    private IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; } = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();

    private IAccounts Accounts => _accounts ??= Services.GetRequiredService<IAccounts>();
    private IAuthBackend AuthBackend => _authBackend ??= Services.GetRequiredService<IAuthBackend>();
    private IDbEntityConverter<DbUser, User> UserConverter => _userConverter ??= Services.DbEntityConverter<DbUser, User>();

    // [ComputeMethod]
    // TODO: move to Features_EnablePhoneAuth
    public virtual Task<bool> IsEnabled(CancellationToken cancellationToken)
        => Task.FromResult(HostInfo.IsDevelopmentInstance || Settings.IsTwilioEnabled);

    // [CommandHandler]
    public virtual async Task<Moment> OnSendTotp(PhoneAuth_SendTotp command, CancellationToken cancellationToken)
    {
        // NOTE(AY): A bit suspicious IApiCommand design:
        // - On one hand, it doesn't have to invalidate anything
        // - On another, it doesn't use a backend.

        // TODO: throttle
        var (session, phone, purpose) = command;
        if (TryGetPredefined(phone, out _))
            return GetExpiresAt(); // no need to send predefined totp

        var (securityToken, modifier) = await GetTotpInputs(session, phone, purpose, cancellationToken).ConfigureAwait(false);
        var totp = Totps.Generate(securityToken, modifier); // generate totp with the newest one
        var expiresAt = GetExpiresAt();

        var sTotp = totp.ToString(TotpFormat, CultureInfo.InvariantCulture);
        await TextMessage.Send(phone, $"Actual Chat: your phone verification code is {sTotp}. Don't share it with anyone.").ConfigureAwait(false);
        return expiresAt;

        DateTimeOffset GetExpiresAt()
            => Clocks.SystemClock.UtcNow + Settings.TotpUIThrottling;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnValidateTotp(
        PhoneAuth_ValidateTotp command,
        CancellationToken cancellationToken)
    {
        var (session, phone, totp) = command;
        if (!await ValidateCode(session, phone, totp, TotpPurpose.SignIn, cancellationToken).ConfigureAwait(false))
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
            var userId = context.Operation.Items.GetOrDefault<UserId>();
            if (!userId.IsNone)
                _ = AuthBackend.GetUser(default, userId, cancellationToken);
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

        var dbUser = await DbUsers.Get(dbContext, account.Id, true, cancellationToken).ConfigureAwait(false);
        if (dbUser == null)
            return default; // Should never happen, but if it somehow does, there is no extra to do in this case

        var user = account.User.WithPhone(phone);
        var conflictingDbUser = await DbUsers.GetByUserIdentity(dbContext, user.GetPhoneIdentity(), false, cancellationToken).ConfigureAwait(false);
        if (conflictingDbUser != null && !OrdinalEquals(conflictingDbUser.Id, dbUser.Id))
            throw StandardError.Unauthorized("Phone number has already been taken by another account.");

        UserConverter.UpdateEntity(user, dbUser);
        context.Operation.Items.Set(account.Id);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ValidateCode(
        Session session,
        Phone phone,
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
        Phone phone,
        TotpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var randomSecret = await TotpSecrets.Get(session, cancellationToken).ConfigureAwait(false);
        var securityTokens = Encoding.UTF8.GetBytes($"{randomSecret}_{session.Id}_{phone}");
        var modifier = $"{purpose}:{phone}";
        return (securityTokens, modifier);
    }

    private bool TryGetPredefined(Phone phone, out int predefinedTotp)
        // removing dashes due to issue with dash in bash env var names
        => Settings.PredefinedTotps.TryGetValue(Phone.Normalize(phone.Value), out predefinedTotp);
}
