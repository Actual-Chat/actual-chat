using System.Text;
using ActualChat.Hosting;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using Stl.Fusion.Authentication.Services;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class PhoneAuth(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IPhoneAuth
{
    private static readonly string TotpFormat = new('0', Constants.Auth.Phone.TotpLength);
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private HostInfo HostInfo { get; } = services.GetRequiredService<HostInfo>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private ISmsGateway Sms { get; } = services.GetRequiredService<ISmsGateway>();
    private Rfc6238AuthenticationService Totps { get; } = services.GetRequiredService<Rfc6238AuthenticationService>();
    private TotpRandomSecrets RandomSecrets { get; } = services.GetRequiredService<TotpRandomSecrets>();
    private IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; } = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();
    private IDbEntityConverter<DbUser, User> UserConverter { get; } = services.DbEntityConverter<DbUser, User>();
    private IAuthBackend AuthBackend { get; } = services.GetRequiredService<IAuthBackend>();

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
        var (session, phone, purpose) = command;
        var (securityToken, modifier) = await GetTotpInputs(session, phone, purpose).ConfigureAwait(false);
        var totp = Totps.GenerateCode(securityToken, modifier); // generate totp with the newest one
        var expiresAt = Clocks.SystemClock.UtcNow + Settings.TotpLifetime;

        var sTotp = totp.ToString(TotpFormat, CultureInfo.InvariantCulture);
        await Sms.Send(phone, $"Actual.chat: your phone verification code is {sTotp}. Don't share it with anyone.").ConfigureAwait(false);
        return expiresAt;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnValidateTotp(
        PhoneAuth_ValidateTotp command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default; // It just spawns other commands, so nothing to do here

        var (session, phone, totp) = command;
        var (securityToken, modifier) = await GetTotpInputs(session, phone, TotpPurpose.SignIn).ConfigureAwait(false);

        if (!Totps.ValidateCode(securityToken, totp, modifier))
            return false;

        var user = new User(Symbol.Empty, string.Empty).WithPhone(phone);
        await Commander
            .Call(new AuthBackend_SignIn(session, user, user.GetPhoneIdentity()), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnVerifyPhone(PhoneAuth_VerifyPhone command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var userId = context.Operation().Items.GetOrDefault(UserId.None);
            if (!userId.IsNone)
                _ = AuthBackend.GetUser(default, userId, cancellationToken);
            return default;
        }

        var (session, phone, totp) = command;
        var (securityToken, modifier) = await GetTotpInputs(session, phone, TotpPurpose.VerifyPhone).ConfigureAwait(false);

        if (!Totps.ValidateCode(securityToken, totp, modifier))
            return false;

        // save phone to account
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account = account with { Phone = phone, IsGreetingCompleted = false };
        await Accounts.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);

        var cmd = new AccountsBackend_Update(account, account.Version);
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);

        // save phone identity + phone claim
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUser = await DbUsers.Get(dbContext, account.Id, true, cancellationToken).ConfigureAwait(false);
        if (dbUser == null)
            return default; // Should never happen, but if it somehow does, there is no extra to do in this case

        var user = account.User.WithPhone(phone);
        var conflictingDbUser = await DbUsers.GetByUserIdentity(dbContext, user.GetPhoneIdentity(), false, cancellationToken).ConfigureAwait(false);
        if (conflictingDbUser != null && !OrdinalEquals(conflictingDbUser.Id, dbUser.Id))
            throw StandardError.Unauthorized("Phone number has already been taken by another account.");

        UserConverter.UpdateEntity(user, dbUser);
        context.Operation().Items.Set(account.Id);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<(byte[] SecurityToken, string Modifier)> GetTotpInputs(Session session, Phone phone, TotpPurpose purpose)
    {
        var randomSecret = await RandomSecrets.Get(session).ConfigureAwait(false);
        var securityTokens = Encoding.UTF8.GetBytes($"{randomSecret}_{session.Id}_{phone}");
        var modifier = $"{purpose}:{phone}";
        return (securityTokens, modifier);
    }
}
