using System.Net.Mail;
using ActualChat.Users.Db;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AccountsBackend : DbServiceBase<UsersDbContext>, IAccountsBackend
{
    private const string AdminEmailDomain = "actual.chat";
    private static HashSet<string> AdminEmails { get; } = new(StringComparer.Ordinal) { "alex.yakunin@gmail.com" };

    private IAuth Auth { get; }
    private IAuthBackend AuthBackend { get; }
    private IAvatarsBackend AvatarsBackend { get; }
    private IServerKvasBackend ServerKvasBackend { get; }
    private IDbEntityResolver<string, DbAccount> DbAccountResolver { get; }

    public AccountsBackend(IServiceProvider services) : base(services)
    {
        Auth = services.GetRequiredService<IAuth>();
        AuthBackend = services.GetRequiredService<IAuthBackend>();
        AvatarsBackend = services.GetRequiredService<IAvatarsBackend>();
        ServerKvasBackend = services.GetRequiredService<IServerKvasBackend>();
        DbAccountResolver = services.GetRequiredService<IDbEntityResolver<string, DbAccount>>();
    }

    // [ComputeMethod]
    public virtual async Task<AccountFull> Get(UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(userId));

        // We _must_ have a dependency on AuthBackend.GetUser here
        var user = await AuthBackend.GetUser(default, userId, cancellationToken).ConfigureAwait(false);
        AccountFull? account;
        if (user == null) {
            account = GetGuestAccount(userId);
            if (account == null)
                throw new ArgumentOutOfRangeException(nameof(userId));
        }
        else {
            var dbAccount = await DbAccountResolver.Get(userId, cancellationToken).ConfigureAwait(false);
            account = dbAccount.Require().ToModel();
            if (IsAdmin(user))
                account = account with { IsAdmin = true };
        }

        // Adding Avatar
        var kvas = ServerKvasBackend.GetUserClient(account);
        var userAvatarSettings = await kvas.GetUserAvatarSettings(cancellationToken).ConfigureAwait(false);
        var avatarId = userAvatarSettings.DefaultAvatarId;
        if (avatarId.IsEmpty) // Default avatar isn't selected - let's pick the first one
            avatarId = userAvatarSettings.AvatarIds.FirstOrDefault();

        var avatar = avatarId.IsEmpty
            ? GetDefaultAvatar(account)
            : await AvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false) // No avatars at all
                ?? GetDefaultAvatar(account);
        account = account with { Avatar = avatar };

        return account;
    }

    // [CommandHandler]
    public virtual async Task Update(
        IAccountsBackend.UpdateCommand command,
        CancellationToken cancellationToken)
    {
        var (account, expectedVersion) = command;
        if (Computed.IsInvalidating()) {
            _ = Get(account.Id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAccount = await dbContext.Accounts.ForUpdate()
            .SingleOrDefaultAsync(a => a.Id == account.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        dbAccount = dbAccount.RequireVersion(expectedVersion);
        dbAccount.UpdateFrom(account);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    internal static bool IsAdmin(User user)
    {
        // TODO(AY): Remove the check relying on test/internal auth providers in the production code
        if (HasIdentity(user, "internal") || HasIdentity(user, "test"))
            return true;

        var email = user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email);
        if (email.IsNullOrEmpty() || !MailAddress.TryCreate(email, out var emailAddress))
            return false;

        if (AdminEmails.Contains(email))
            return true; // Predefined admin email
        if (HasGoogleIdentity(user) && OrdinalEquals(emailAddress.Host, AdminEmailDomain))
            return true; // actual.chat email
        return false;
    }

    private static bool HasGoogleIdentity(User user)
        => HasIdentity(user, GoogleDefaults.AuthenticationScheme);

    private static bool HasIdentity(User user, string provider)
        => user.Identities.Keys.Select(x => x.Schema).Contains(provider, StringComparer.Ordinal);

    private static AccountFull? GetGuestAccount(UserId userId)
    {
        if (userId.IsEmpty || !userId.IsGuestId)
            return null;

        var name = RandomNameGenerator.Default.Generate(userId);
        var user = new User(userId, name);
        var account = new AccountFull(user);
        return account;
    }

    private static AvatarFull GetDefaultAvatar(AccountFull account)
        => new() {
            Id = default,
            PrincipalId = account.Id,
            Name = account.User.Name,
            Picture = DefaultUserPicture.Get(account.User),
            Bio = "",
        };
}
