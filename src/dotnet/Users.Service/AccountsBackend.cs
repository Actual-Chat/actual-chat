using System.Net.Mail;
using ActualChat.Chat;
using ActualChat.Commands;
using ActualChat.Users.Db;
using ActualChat.Users.Events;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AccountsBackend(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IAccountsBackend
{
    private IAuthBackend? _authBackend;
    private IAvatarsBackend? _avatarsBackend;
    private IServerKvasBackend? _serverKvasBackend;
    private GreetingDispatcher? _greetingDispatcher;
    private IDbEntityResolver<string, DbAccount>? _dbAccountResolver;
    private const string AdminEmailDomain = "actual.chat";
    private static HashSet<string> AdminEmails { get; } = new(StringComparer.Ordinal) { "alex.yakunin@gmail.com" };

    private IAuthBackend AuthBackend => _authBackend ??= Services.GetRequiredService<IAuthBackend>();
    private IAvatarsBackend AvatarsBackend => _avatarsBackend ??= Services.GetRequiredService<IAvatarsBackend>();
    private IServerKvasBackend ServerKvasBackend => _serverKvasBackend ??= Services.GetRequiredService<IServerKvasBackend>();
    private GreetingDispatcher GreetingDispatcher => _greetingDispatcher ??= Services.GetRequiredService<GreetingDispatcher>();
    private IDbEntityResolver<string, DbAccount> DbAccountResolver => _dbAccountResolver ??= Services.GetRequiredService<IDbEntityResolver<string, DbAccount>>();

    // [ComputeMethod]
    public virtual async Task<AccountFull?> Get(UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsNone)
            return null;

        // We _must_ have a dependency on AuthBackend.GetUser here
        var user = await AuthBackend.GetUser(default, userId, cancellationToken).ConfigureAwait(false);
        AccountFull? account;
        if (user == null) {
            account = GetGuestAccount(userId);
            if (account == null)
                return null;
        }
        else {
            var dbAccount = await DbAccountResolver.Get(userId, cancellationToken).ConfigureAwait(false);
            account = dbAccount?.ToModel(user);
            if (account == null)
                return null;

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

    // [ComputeMethod]
    public virtual async Task<UserId> GetIdByUserIdentity(UserIdentity identity, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var sid = identity.Id.Value;
        var dbUserIdentity = await dbContext.UserIdentities
            .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken)
            .ConfigureAwait(false);
        return new UserId(dbUserIdentity?.DbUserId);
    }

    // [CommandHandler]
    public virtual async Task OnUpdate(
        AccountsBackend_Update command,
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
            .FirstOrDefaultAsync(a => a.Id == account.Id, cancellationToken)
            .ConfigureAwait(false);
        dbAccount = dbAccount.RequireVersion(expectedVersion);
        var needsGreeting = dbAccount.IsGreetingCompleted && !account.IsGreetingCompleted;
        dbAccount.UpdateFrom(account);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (needsGreeting)
            GreetingDispatcher.OnGreetingNeeded();
    }

    // [CommandHandler]
    public virtual async Task OnDelete(
        AccountsBackend_Delete command,
        CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Computed.IsInvalidating()) {
            _ = Get(userId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.UserPresences
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Avatars
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.UserIdentities
            .Where(a => a.DbUserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Users
            .Where(a => a.Id == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.Accounts
            .Where(a => a.Id == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // authors
        var removeAuthorsCommand = new AuthorsBackend_Remove(ChatId.None, AuthorId.None, userId);
        await Commander.Call(removeAuthorsCommand, true, cancellationToken).ConfigureAwait(false);
    }

    [EventHandler]
    public virtual Task OnNewUserEvent(NewUserEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask; // It just notifies GreetingDispatcher

        GreetingDispatcher.OnGreetingNeeded();
        return Task.CompletedTask;
    }

    // Private methods

    internal static bool IsAdmin(User user)
    {
        // TODO(AY): Remove the check relying on test/internal auth providers in the production code
        if (HasIdentity(user, "internal") || HasIdentity(user, "test"))
            return true;

        var email = user.GetEmail();
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
        if (!userId.IsGuest)
            return null;

        var name = RandomNameGenerator.Default.Generate(userId);
        var user = new User(userId, name);
        var account = new AccountFull(user);
        return account;
    }

    private static AvatarFull GetDefaultAvatar(AccountFull account)
        => new(account.Id) {
            Name = account.FullName,
            PictureUrl = DefaultUserPicture.Get(account.FullName),
            Bio = "",
        };
}
