using System.Net.Mail;
using ActualChat.Users.Db;
using Microsoft.AspNetCore.Authentication.Google;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AccountsBackend : DbServiceBase<UsersDbContext>, IAccountsBackend
{
    private const string AdminEmailDomain = "actual.chat";
    private static HashSet<string> AdminEmails { get; } = new(StringComparer.Ordinal) { "alex.yakunin@gmail.com" };

    private IAuthBackend AuthBackend { get; }
    private IAvatarsBackend AvatarsBackend { get; }
    private IServerKvasBackend ServerKvasBackend { get; }
    private IDbEntityResolver<string, DbAccount> DbAccountResolver { get; }

    public AccountsBackend(IServiceProvider services) : base(services)
    {
        AuthBackend = services.GetRequiredService<IAuthBackend>();
        AvatarsBackend = services.GetRequiredService<IAvatarsBackend>();
        ServerKvasBackend = services.GetRequiredService<IServerKvasBackend>();
        DbAccountResolver = services.GetRequiredService<IDbEntityResolver<string, DbAccount>>();
    }

    // [ComputeMethod]
    public virtual async Task<AccountFull?> Get(string id, CancellationToken cancellationToken)
    {
        if (id.IsNullOrEmpty())
            return null;

        // We _must_ have a dependency on AuthBackend.GetUser here
        var user = await AuthBackend.GetUser(default, id, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return null;

        var dbAccount = await DbAccountResolver.Get(id, cancellationToken).Require().ConfigureAwait(false);
        var account = new AccountFull(user.Id, user) {
            IsAdmin = IsAdmin(user),
        };
        account = dbAccount.ToModel(account);

        // Adding Avatar
        var kvas = ServerKvasBackend.GetClient(ServerKvasBackend.GetUserPrefix(account.Id));
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
        var account = command.Account;
        if (Computed.IsInvalidating()) {
            _ = Get(account.Id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAccount = await dbContext.Accounts
            .Get(account.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? new DbAccount();
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

    private AvatarFull GetDefaultAvatar(AccountFull account)
        => new() {
            Id = default,
            ChatPrincipalId = account.Id,
            Name = account.User.Name,
            Picture = DefaultPicture.Get(account),
            Bio = "",
        };
}
