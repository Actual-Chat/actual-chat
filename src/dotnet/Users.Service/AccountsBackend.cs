using System.Net.Mail;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using Microsoft.AspNetCore.Authentication.Google;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AccountsBackend : DbServiceBase<UsersDbContext>, IAccountsBackend
{
    private const string AdminEmailDomain = "actual.chat";
    private static HashSet<string> AdminEmails { get; } = new(StringComparer.Ordinal) { "alex.yakunin@gmail.com" };

    private UsersSettings UsersSettings { get; }
    private IAuthBackend AuthBackend { get; }
    private IUserAvatarsBackend UserAvatarsBackend { get; }
    private IDbEntityResolver<string, DbAccount> DbAccountResolver { get; }

    public AccountsBackend(IServiceProvider services) : base(services)
    {
        UsersSettings = services.GetRequiredService<UsersSettings>();
        AuthBackend = services.GetRequiredService<IAuthBackend>();
        UserAvatarsBackend = services.GetRequiredService<IUserAvatarsBackend>();
        DbAccountResolver = services.GetRequiredService<IDbEntityResolver<string, DbAccount>>();
    }

    // [ComputeMethod]
    public virtual async Task<Account?> Get(string id, CancellationToken cancellationToken)
    {
        var user = await AuthBackend.GetUser(default, id, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return null;

        var isAdmin = IsAdmin(user);
        var account =  new Account(user.Id, user) {
            IsAdmin = isAdmin,
            Status = isAdmin ? AccountStatus.Active : UsersSettings.NewAccountStatus,
        };

        var dbAccount = await DbAccountResolver.Get(id, cancellationToken).ConfigureAwait(false);
        if (dbAccount != null)
            return dbAccount.ToModel(account);

        // Let's create account on demand
        _ = Commander.Call(new IAccountsBackend.UpdateCommand(account), true, CancellationToken.None);
        return account;
    }

    public virtual async Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty())
            return null;

        var user = await AuthBackend.GetUser(default, userId, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return null;

        var userAuthor = new UserAuthor { Id = user.Id, Name = user.Name };

        var account = await Get(userId, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return userAuthor;

        if (!account.AvatarId.IsEmpty) {
            var avatar = await UserAvatarsBackend.Get(account.AvatarId, cancellationToken).ConfigureAwait(false);
            if (avatar != null) {
                userAuthor = userAuthor with { Picture = avatar.Picture };
            }
        }
        if (userAuthor.Picture.IsNullOrEmpty())
            userAuthor = userAuthor with { Picture = GetDefaultPicture(user) };
        return userAuthor;
    }

    // [CommandHandler]
    public virtual async Task Update(
        IAccountsBackend.UpdateCommand command,
        CancellationToken cancellationToken)
    {
        var account = command.Account;
        if (Computed.IsInvalidating()) {
            _ = Get(account.Id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAccount = await dbContext.Accounts
            .FindAsync(DbKey.Compose((string)account.Id), cancellationToken)
            .ConfigureAwait(false)
            ?? new DbAccount();
        dbAccount.UpdateFrom(account);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private string GetDefaultPicture(User? user, int size = 80)
    {
        if (user == null) return "";

        var email = (user.Claims.GetValueOrDefault(System.Security.Claims.ClaimTypes.Email) ?? "")
            .Trim().ToLowerInvariant();
        if (!email.IsNullOrEmpty()) {
            var emailHash = email.GetMD5HashCode().ToLowerInvariant();
            return $"https://www.gravatar.com/avatar/{emailHash}?s={size}";
        }
        return "";
    }

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
}
